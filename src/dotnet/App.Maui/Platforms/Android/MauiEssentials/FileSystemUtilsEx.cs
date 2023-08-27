namespace ActualChat.App.Maui;

public static class FileSystemUtilsEx
{
    private const string EssentialsFolderHash = "2203693cc04e0be7f4f024d5f9499e13";

    public static Java.IO.File GetTemporaryFile(Java.IO.File root, string fileName)
    {
        // A copy from https://github.com/dotnet/maui/blob/main/src/Essentials/src/FileSystem/FileSystemUtils.android.cs#L36

        // create the directory for all Essentials files
        var rootDir = new Java.IO.File(root, EssentialsFolderHash);
        rootDir.Mkdirs();
        rootDir.DeleteOnExit();

        // create a unique directory just in case there are multiple file with the same name
        var tmpDir = new Java.IO.File(rootDir, Guid.NewGuid().ToString("N"));
        tmpDir.Mkdirs();
        tmpDir.DeleteOnExit();

        // create the new temporary file
        var tmpFile = new Java.IO.File(tmpDir, fileName);
        tmpFile.DeleteOnExit();

        return tmpFile;
    }
}
