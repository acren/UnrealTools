using System.Collections.Generic;
using System.IO;
using System.Threading;
using FileMaterialization;

namespace SystemUtilities.IO;

public static partial class FileUtils
{
    /// <summary>
    /// Copies one named subdirectory from a source root into the matching location beneath the destination root.
    /// </summary>
    public static void CopySubdirectory(string sourcePath, string destinationPath, string subdirectory)
    {
        Directory.CreateDirectory(Path.Combine(destinationPath, subdirectory));
        DirectoryMaterializer.Copy(Path.Combine(sourcePath, subdirectory), Path.Combine(destinationPath, subdirectory));
    }

    /// <summary>
    /// Copies one named file from a source directory into a destination directory.
    /// </summary>
    public static void CopyFile(string sourceDirectoryPath, string destinationDirectoryPath, string fileName, bool errorIfSourceMissing = true, bool overwrite = false)
    {
        CopyFile(Path.Combine(sourceDirectoryPath, fileName), destinationDirectoryPath, errorIfSourceMissing, overwrite);
    }

    /// <summary>
    /// Copies one file into a destination directory, optionally tolerating a missing source or overwriting the target.
    /// </summary>
    public static void CopyFile(string sourceFilePath, string destinationDirectoryPath, bool errorIfSourceMissing = true, bool overwrite = false)
    {
        if (!File.Exists(sourceFilePath) && !errorIfSourceMissing)
        {
            return;
        }

        string fileName = Path.GetFileName(sourceFilePath);
        string destinationFilePath = Path.Combine(destinationDirectoryPath, fileName);
        if (overwrite)
        {
            DeleteFileIfExists(destinationFilePath);
        }

        File.Copy(sourceFilePath, destinationFilePath);
    }
}
