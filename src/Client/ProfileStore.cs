// SPDX-License-Identifier: MIT
// profiles.json in the mod's data folder: read once at the start, written after every change (to a temporary file,
// then moved over the old one, so a crash never leaves half a file). A file that is not ours or cannot be read is
// kept beside as profiles.json.bad and the built-ins start again. No Kiln calls; the tick thread calls it.
namespace KilnAutoLoot.Client;

internal sealed class ProfileStore(string directory)
{
    public const string FileName = "profiles.json";

    public string FilePath => Path.Combine(directory, FileName);

    /// <summary>The book from the file; when there is none (the first start) or it is unusable, the seeded one, and
    /// <paramref name="seeded"/> is true (the caller saves it).</summary>
    public ProfileBook Load(Core.PersonalSettings inUse, string mineName, out bool seeded, out string? problem)
    {
        problem = null;
        seeded = false;
        string? text = null;
        try
        {
            if (File.Exists(FilePath)) text = File.ReadAllText(FilePath);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            problem = $"{FileName} cannot be read: {e.Message}";
        }
        if (text is not null && ProfileBook.FromJson(text) is { } book) return book;
        if (text is not null)
        {
            problem = $"{FileName} is not a profile file of this mod; kept as {FileName}.bad";
            try
            {
                File.Copy(FilePath, FilePath + ".bad", overwrite: true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                problem += $" (could not copy it: {e.Message})";
            }
        }
        seeded = true;
        return ProfileBook.Seed(inUse, mineName);
    }

    /// <summary>Writes the book; null when it worked, else why not.</summary>
    public string? Save(ProfileBook book)
    {
        var tmp = FilePath + ".tmp";
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(tmp, book.ToJson());
            File.Move(tmp, FilePath, overwrite: true);
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return e.Message;
        }
    }
}
