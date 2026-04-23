// Pairs every Nerdbank open-generic converter with the data type it converts. PolyType's
// source generator picks these up at compile time and, for each closed instance of the data
// type that ends up in any witness, also emits a closed shape for the converter — so
// Nerdbank's TryGetRuntimeProfferedConverter can instantiate the converter via the codegen
// shape's GetAssociatedTypeShape callback (no reflection emit needed).
//
// Without these declarations, source-gen returns null from GetAssociatedTypeShape and Nerdbank
// can't resolve the open-generic converter — surfaces as the misleading
// "delayed value that has not been completed" cache error mid-graph-walk.
//
// Add a new entry whenever a new open-generic converter is registered in Fusion's
// CreateDefaultSerializer ConverterTypes list or in NerdbankSerializerSetup's extraOpenGenerics.

using System.Collections.Immutable;
using ActualChat.Serialization.Internal;
using ActualLab.Rpc;
using ActualLab.Serialization;
using ActualLab.Serialization.Internal;

// Fusion-side data ↔ converter pairings (open-generic converters live in
// ActualLab.Serialization.NerdbankMessagePack):
[assembly: TypeShapeExtension(typeof(Option<>), AssociatedTypes = [typeof(OptionNerdbankConverter<>)])]
[assembly: TypeShapeExtension(typeof(Result<>), AssociatedTypes = [typeof(ResultNerdbankConverter<>)])]
[assembly: TypeShapeExtension(typeof(ApiOption<>), AssociatedTypes = [typeof(ApiOptionNerdbankConverter<>)])]
[assembly: TypeShapeExtension(typeof(ApiNullable<>), AssociatedTypes = [typeof(ApiNullableNerdbankConverter<>)])]
[assembly: TypeShapeExtension(typeof(ApiNullable8<>), AssociatedTypes = [typeof(ApiNullable8NerdbankConverter<>)])]
[assembly: TypeShapeExtension(typeof(ApiArray<>), AssociatedTypes = [typeof(ApiArrayNerdbankConverter<>)])]
[assembly: TypeShapeExtension(typeof(ApiMap<,>), AssociatedTypes = [typeof(ApiMapNerdbankConverter<,>)])]
[assembly: TypeShapeExtension(typeof(RpcStream<>), AssociatedTypes = [typeof(RpcStreamNerdbankConverter<>)])]
[assembly: TypeShapeExtension(typeof(TypeDecoratingUniSerialized<>), AssociatedTypes = [typeof(TypeDecoratingUniSerializedNerdbankConverter<>)])]
[assembly: TypeShapeExtension(typeof(NewtonsoftJsonSerialized<>), AssociatedTypes = [typeof(NewtonsoftJsonSerializedNerdbankConverter<>)])]

// ActualChat-side data ↔ converter pairings:
[assembly: TypeShapeExtension(typeof(Range<>), AssociatedTypes = [typeof(RangeNerdbankConverter<>)])]
[assembly: TypeShapeExtension(typeof(SetDiff<>), AssociatedTypes = [typeof(SetDiffNerdbankConverter<>)])]
[assembly: TypeShapeExtension(typeof(SetDiff<,>), AssociatedTypes = [typeof(SetDiffNerdbankConverter<,>)])]
[assembly: TypeShapeExtension(typeof(Change<>), AssociatedTypes = [typeof(ChangeNerdbankConverter<>)])]
[assembly: TypeShapeExtension(typeof(Change<,>), AssociatedTypes = [typeof(ChangeNerdbankConverter<,>)])]
[assembly: TypeShapeExtension(typeof(Expiring<>), AssociatedTypes = [typeof(ExpiringNerdbankConverter<>)])]
[assembly: TypeShapeExtension(typeof(Trimmed<>), AssociatedTypes = [typeof(TrimmedNerdbankConverter<>)])]

// Framework wrapper types with our own converters — codegen-only clients can deserialize
// any closed instance of these without enumerating closed forms in a witness, because the
// associated converter handles each closed form via its inner GetConverter<T> call.
//   Nullable<T>    → wire-compatible with the natural OptionalShape encoding (nil/value).
//   Box<T>         → strips the box on the wire (just T, no envelope).
//   Maybe<T>       → nil/[]/[value] tri-state, mirrors OptionNerdbankConverter<T>.
//   IImmutableDictionary<K,V> → msgpack map; tolerates legacy [[k,v],…] arrays on read.
[assembly: TypeShapeExtension(typeof(Nullable<>), AssociatedTypes = [typeof(NullableNerdbankConverter<>)])]
[assembly: TypeShapeExtension(typeof(Box<>), AssociatedTypes = [typeof(BoxNerdbankConverter<>)])]
[assembly: TypeShapeExtension(typeof(Maybe<>), AssociatedTypes = [typeof(MaybeNerdbankConverter<>)])]
[assembly: TypeShapeExtension(typeof(IImmutableDictionary<,>), AssociatedTypes = [typeof(ImmutableDictionaryNerdbankConverter<,>)])]
