using System.Collections.Immutable;
using System.IO.Compression;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace ActualChat.App.AotHelper;

/// <summary>
/// Writes a crossgen2 <c>.mibc</c> profile from reflection.
/// <para>
/// A .mibc is a zip holding one managed assembly whose global methods carry
/// <c>ldtoken &lt;method&gt;; pop</c> sequences; crossgen2 roots every method named there.
/// It is the only mechanism that can name an exact generic instantiation, which is what
/// a full R2R build cannot enumerate on its own — see docs/ios-specific.md.
/// </para>
/// <para>
/// The layout mirrors dotnet/runtime's <c>MibcEmitter</c>. Two details are load-bearing:
/// methods are grouped by the assemblies their signature touches, and the reader
/// (<c>MIbcProfileParser</c>) discards a whole group unless every assembly named in the
/// group is inside the compilation's version bubble.
/// </para>
/// </summary>
public sealed class MibcBuilder(string assemblyName)
{
    private readonly MetadataBuilder _metadata = new();
    private readonly BlobBuilder _ilBuilder = new();
    private readonly Dictionary<string, AssemblyReferenceHandle> _assemblyRefs = new(StringComparer.Ordinal);
    private readonly Dictionary<Type, EntityHandle> _typeRefs = new();
    private readonly Dictionary<MethodBase, EntityHandle> _methodRefs = new();
    private readonly HashSet<MethodBase> _added = new();
    private readonly SortedDictionary<string, InstructionEncoder> _groups = new(StringComparer.Ordinal);
    private MethodBodyStreamEncoder _methodBodies;
    private BlobHandle _globalMethodSig;
    private Blob _mvidFixup;
    private int _methodCount;

    public int MethodCount => _methodCount;
    public int GroupCount => _groups.Count;

    public void Add(MethodBase method)
    {
        if (!_added.Add(method))
            return;

        var groupName = GetGroupName(method);
        if (groupName == null)
            return;

        if (!_groups.TryGetValue(groupName, out var il)) {
            il = new InstructionEncoder(new BlobBuilder());
            _groups.Add(groupName, il);
        }
        EntityHandle handle;
        try {
            handle = GetMethodHandle(method);
        }
        catch (NotSupportedException) {
            return;
        }
        il.OpCode(ILOpCode.Ldtoken);
        il.Token(handle);
        il.OpCode(ILOpCode.Pop);
        _methodCount++;
    }

    public void Save(string filePath)
    {
        var fileName = Path.GetFileName(filePath);
        var dictionary = new InstructionEncoder(new BlobBuilder());
        var index = 0;
        foreach (var (name, il) in _groups) {
            var methodName = "Assemblies_" + name;
            if (methodName.Length > 200)
                methodName = methodName[..200];
            var handle = AddGlobalMethod($"{methodName}_{++index}", il);
            dictionary.LoadString(_metadata.GetOrAddUserString(name));
            dictionary.OpCode(ILOpCode.Ldtoken);
            dictionary.Token(handle);
            dictionary.OpCode(ILOpCode.Pop);
        }
        AddGlobalMethod("AssemblyDictionary", dictionary);

        var peStream = new MemoryStream();
        var peBuilder = new ManagedPEBuilder(
            new PEHeaderBuilder(),
            new MetadataRootBuilder(_metadata),
            _ilBuilder,
            deterministicIdProvider: _ => ContentId);
        var peBlob = new BlobBuilder();
        var contentId = peBuilder.Serialize(peBlob);
        new BlobWriter(_mvidFixup).WriteGuid(contentId.Guid);
        peBlob.WriteContentTo(peStream);
        peStream.Position = 0;

        File.Delete(filePath);
        using var zip = ZipFile.Open(filePath, ZipArchiveMode.Create);
        using var entry = zip.CreateEntry(fileName + ".dll", CompressionLevel.Optimal).Open();
        peStream.CopyTo(entry);
    }

    // Metadata scaffolding

    private static readonly BlobContentId ContentId =
        new(new Guid("97F4DBD4-F6D1-4FAD-91B3-1001F92068E5"), 0x04030201);

    private void EnsureInitialized()
    {
        if (!_globalMethodSig.IsNil)
            return;

        _methodBodies = new MethodBodyStreamEncoder(_ilBuilder);
        var nameHandle = _metadata.GetOrAddString(assemblyName);
        var mvid = _metadata.ReserveGuid();
        _mvidFixup = mvid.Content;
        _metadata.AddModule(0, nameHandle, mvid.Handle, default, default);
        _metadata.AddAssembly(nameHandle, new Version(0, 0, 0, 0), default, default, default, AssemblyHashAlgorithm.None);
        _metadata.AddTypeDefinition(
            default, default, _metadata.GetOrAddString("<Module>"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));

        var sig = new BlobBuilder();
        new BlobEncoder(sig).MethodSignature(SignatureCallingConvention.Default, 0, false);
        sig.WriteCompressedInteger(0);
        sig.WriteByte((byte)SignatureTypeCode.Void);
        _globalMethodSig = _metadata.GetOrAddBlob(sig);
    }

    private MethodDefinitionHandle AddGlobalMethod(string name, InstructionEncoder il)
    {
        EnsureInitialized();
        var offset = _methodBodies.AddMethodBody(il, maxStack: 8);
        return _metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL,
            _metadata.GetOrAddString(name),
            _globalMethodSig,
            offset,
            default);
    }

    private AssemblyReferenceHandle GetAssemblyRef(Assembly assembly)
    {
        var name = assembly.GetName();
        var key = name.FullName;
        if (_assemblyRefs.TryGetValue(key, out var handle))
            return handle;

        var token = name.GetPublicKeyToken();
        handle = _metadata.AddAssemblyReference(
            _metadata.GetOrAddString(name.Name!),
            name.Version ?? new Version(0, 0, 0, 0),
            culture: default,
            publicKeyOrToken: token is { Length: > 0 } ? _metadata.GetOrAddBlob(token) : default,
            flags: default,
            hashValue: default);
        _assemblyRefs.Add(key, handle);
        return handle;
    }

    private EntityHandle GetTypeHandle(Type type)
    {
        EnsureInitialized();
        if (_typeRefs.TryGetValue(type, out var handle))
            return handle;

        var isTypeReference = type.IsGenericTypeDefinition
            || (!type.IsGenericType && !type.HasElementType && !type.IsGenericParameter);
        if (isTypeReference) {
            // A nested TypeRef must carry an empty namespace - it belongs to the outermost type only
            // (ECMA-335 II.22.38). Reflection reports the enclosing namespace on nested types, and
            // writing that out yields a TypeRef nothing can resolve.
            var declaringType = type.DeclaringType;
            var scope = declaringType != null
                ? GetTypeHandle(declaringType)
                : GetAssemblyRef(type.Assembly);
            handle = _metadata.AddTypeReference(
                scope,
                declaringType == null && type.Namespace is { } ns ? _metadata.GetOrAddString(ns) : default,
                _metadata.GetOrAddString(type.Name));
        }
        else {
            var blob = new BlobBuilder();
            EncodeType(blob, type);
            handle = _metadata.AddTypeSpecification(_metadata.GetOrAddBlob(blob));
        }
        _typeRefs.Add(type, handle);
        return handle;
    }

    private EntityHandle GetMethodHandle(MethodBase method)
    {
        EnsureInitialized();
        if (_methodRefs.TryGetValue(method, out var handle))
            return handle;

        if (method is MethodInfo { IsGenericMethod: true, IsGenericMethodDefinition: false } generic) {
            var parent = GetMethodHandle(generic.GetGenericMethodDefinition());
            var blob = new BlobBuilder();
            new BlobEncoder(blob).MethodSpecificationSignature(generic.GetGenericArguments().Length);
            foreach (var argument in generic.GetGenericArguments())
                EncodeType(blob, argument);
            handle = _metadata.AddMethodSpecification(parent, _metadata.GetOrAddBlob(blob));
        }
        else {
            var typical = GetTypicalDefinition(method);
            var signature = new BlobBuilder();
            EncodeMethodSignature(signature, typical);
            handle = _metadata.AddMemberReference(
                GetTypeHandle(method.DeclaringType!),
                _metadata.GetOrAddString(method.Name),
                _metadata.GetOrAddBlob(signature));
        }
        _methodRefs.Add(method, handle);
        return handle;
    }

    // Signature encoding (ECMA-335 II.23.2)

    private static MethodBase GetTypicalDefinition(MethodBase method)
    {
        // A MemberRef whose parent is a TypeSpec must carry the *open* signature of the
        // declaring type's definition (`!0`, `!1`), not the substituted one.
        try {
            return method.Module.ResolveMethod(method.MetadataToken) ?? method;
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or NotSupportedException) {
            return method;
        }
    }

    private void EncodeMethodSignature(BlobBuilder blob, MethodBase method)
    {
        if ((method.CallingConvention & CallingConventions.VarArgs) != 0)
            throw new NotSupportedException("Vararg methods cannot be named in a .mibc.");

        var parameters = method.GetParameters();
        var genericCount = method.IsGenericMethodDefinition ? method.GetGenericArguments().Length : 0;
        var header = (byte)SignatureCallingConvention.Default;
        if (!method.IsStatic)
            header |= (byte)SignatureAttributes.Instance;
        if (genericCount > 0)
            header |= (byte)SignatureAttributes.Generic;
        blob.WriteByte(header);
        if (genericCount > 0)
            blob.WriteCompressedInteger(genericCount);
        blob.WriteCompressedInteger(parameters.Length);

        if (method is MethodInfo methodInfo)
            EncodeType(blob, methodInfo.ReturnType);
        else
            blob.WriteByte((byte)SignatureTypeCode.Void);
        foreach (var parameter in parameters)
            EncodeType(blob, parameter.ParameterType);
    }

    private void EncodeType(BlobBuilder blob, Type type)
    {
        if (type.IsByRef) {
            blob.WriteByte((byte)SignatureTypeCode.ByReference);
            EncodeType(blob, type.GetElementType()!);
            return;
        }
        if (type.IsPointer) {
            blob.WriteByte((byte)SignatureTypeCode.Pointer);
            EncodeType(blob, type.GetElementType()!);
            return;
        }
        if (type.IsSZArray) {
            blob.WriteByte((byte)SignatureTypeCode.SZArray);
            EncodeType(blob, type.GetElementType()!);
            return;
        }
        if (type.IsArray) {
            blob.WriteByte((byte)SignatureTypeCode.Array);
            EncodeType(blob, type.GetElementType()!);
            blob.WriteCompressedInteger(type.GetArrayRank());
            blob.WriteCompressedInteger(0);
            blob.WriteCompressedInteger(0);
            return;
        }
        if (type.IsGenericParameter) {
            blob.WriteByte((byte)(type.IsGenericMethodParameter
                ? SignatureTypeCode.GenericMethodParameter
                : SignatureTypeCode.GenericTypeParameter));
            blob.WriteCompressedInteger(type.GenericParameterPosition);
            return;
        }
        if (type.IsFunctionPointer)
            throw new NotSupportedException("Function pointer signatures cannot be named in a .mibc.");

        if (PrimitiveTypeCodes.TryGetValue(type, out var code)) {
            blob.WriteByte((byte)code);
            return;
        }
        if (type.IsConstructedGenericType) {
            blob.WriteByte((byte)SignatureTypeCode.GenericTypeInstance);
            blob.WriteByte((byte)(type.IsValueType ? SignatureTypeKind.ValueType : SignatureTypeKind.Class));
            var definition = type.GetGenericTypeDefinition();
            blob.WriteCompressedInteger(CodedIndex.TypeDefOrRefOrSpec(GetTypeHandle(definition)));
            var arguments = type.GetGenericArguments();
            blob.WriteCompressedInteger(arguments.Length);
            foreach (var argument in arguments)
                EncodeType(blob, argument);
            return;
        }
        blob.WriteByte((byte)(type.IsValueType ? SignatureTypeKind.ValueType : SignatureTypeKind.Class));
        blob.WriteCompressedInteger(CodedIndex.TypeDefOrRefOrSpec(GetTypeHandle(type)));
    }

    private static readonly Dictionary<Type, SignatureTypeCode> PrimitiveTypeCodes = new() {
        [typeof(void)] = SignatureTypeCode.Void,
        [typeof(bool)] = SignatureTypeCode.Boolean,
        [typeof(char)] = SignatureTypeCode.Char,
        [typeof(sbyte)] = SignatureTypeCode.SByte,
        [typeof(byte)] = SignatureTypeCode.Byte,
        [typeof(short)] = SignatureTypeCode.Int16,
        [typeof(ushort)] = SignatureTypeCode.UInt16,
        [typeof(int)] = SignatureTypeCode.Int32,
        [typeof(uint)] = SignatureTypeCode.UInt32,
        [typeof(long)] = SignatureTypeCode.Int64,
        [typeof(ulong)] = SignatureTypeCode.UInt64,
        [typeof(float)] = SignatureTypeCode.Single,
        [typeof(double)] = SignatureTypeCode.Double,
        [typeof(string)] = SignatureTypeCode.String,
        [typeof(object)] = SignatureTypeCode.Object,
        [typeof(nint)] = SignatureTypeCode.IntPtr,
        [typeof(nuint)] = SignatureTypeCode.UIntPtr,
        [typeof(TypedReference)] = SignatureTypeCode.TypedReference,
    };

    // Grouping

    private static string? GetGroupName(MethodBase method)
    {
        if (method.DeclaringType is not { } declaringType)
            return null;

        var assemblies = new HashSet<string>(StringComparer.Ordinal);
        var definingAssembly = declaringType.Assembly.GetName().Name;
        if (definingAssembly == null)
            return null;

        assemblies.Add(definingAssembly);
        AddAssociatedAssemblies(declaringType, assemblies);
        if (method.IsGenericMethod && !method.IsGenericMethodDefinition)
            foreach (var argument in method.GetGenericArguments())
                AddAssociatedAssemblies(argument, assemblies);

        assemblies.Remove(definingAssembly);
        var others = assemblies.ToArray();
        Array.Sort(others, StringComparer.Ordinal);
        return string.Concat(definingAssembly, ";", string.Join(";", others), others.Length > 0 ? ";" : "");
    }

    private static void AddAssociatedAssemblies(Type type, HashSet<string> assemblies)
    {
        if (type.IsPrimitive || type.IsGenericParameter)
            return;
        if (type.HasElementType) {
            AddAssociatedAssemblies(type.GetElementType()!, assemblies);
            return;
        }
        if (type.Assembly.GetName().Name is { } name)
            assemblies.Add(name);
        foreach (var argument in type.GetGenericArguments())
            AddAssociatedAssemblies(argument, assemblies);
    }
}
