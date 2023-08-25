using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

public static class LocaUtils
{
    /// <summary>
    /// Builds a dictionary from the .loca file at the specified path.
    /// </summary>
    /// <param name="filePath">The path to the .loca file.</param>
    /// <returns>A dictionary of localization id -> text.</returns>
    public static IDictionary<string, string> DictionaryFromFile(string filePath)
    {
        var localizations = new Dictionary<string, string>();
        using (var stream = openLocaFile(filePath))
        {
            var prevOffset = 0;
            var baseTextAddr = getBaseTextAddress(stream);
            while (stream.Position < baseTextAddr)
            {
                var (idStr, extraIdPartsLength) = getIdFromStream(stream);

                var nextOffset = getNextOffset(extraIdPartsLength, stream);
                var prevPos = stream.Position;

                localizations.Add(
                    idStr,
                    getLocalizedText(baseTextAddr, nextOffset, prevOffset, stream)
                );

                stream.Position = prevPos;
                prevOffset += nextOffset;
            }
        }

        return localizations;
    }

    /// <summary>
    /// Exports the loca json to the specified file.
    /// </summary>
    /// <param name="destinationPath">The destination file to write the JSON to.</param>
    /// <param name="locaFilePath">The loca file to parse.</param>
    public static void ExportToFile(string destinationPath, string locaFilePath)
    {
        using (var destFile = File.OpenWrite(destinationPath))
        {
            var locaJson = JsonFromFile(locaFilePath);
            var locaBytes = Encoding.UTF8.GetBytes(locaJson);

            destFile.Write(locaBytes, 0, locaBytes.Length);
        }
    }

    /// <summary>
    /// Returns the localizations from the specified .loca file as a json string.
    /// </summary>
    /// <param name="filePath">The path to the .loca file.</param>
    /// <returns>A JSON string representing the localizations at the file path.</returns>
    public static string JsonFromFile(string filePath)
    {
        return JsonSerializer.Serialize(
            DictionaryFromFile(filePath),
            new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                WriteIndented = true
            }
        );
    }

    private static int getBaseTextAddress(
        MemoryStream stream,
        int baseAddrLength = 4,
        int headerLength = 8
    )
    {
        // Skip 'LOCA' and the next 4 bytes (version?)
        stream.ReadExactly(new byte[headerLength], 0, headerLength);

        // The next 4 bytes is the base address of the text records in the lookup.
        var baseAddrBytes = new byte[baseAddrLength];
        stream.ReadExactly(baseAddrBytes, 0, baseAddrBytes.Length);
        return BitConverter.ToInt32(baseAddrBytes, 0);
    }

    private static (string, int) getIdFromStream(MemoryStream stream, int idLength = 37)
    {
        // Id is 37 bytes, and may contain _%d at the end to indicate which part.
        var idBytes = new byte[idLength];
        stream.ReadExactly(idBytes, 0, idBytes.Length);
        return getIdWithExtra(stream, Encoding.ASCII.GetString(idBytes));
    }

    private static (string, int) getIdWithExtra(MemoryStream stream, string idStr)
    {
        var idWithExtra = idStr;

        // Is the next character a "_"? If so, read until a \0 is encountered.
        var extraIdPartsLength = 0;
        var currentPos = stream.Position;
        if (stream.ReadByte() == '_')
        {
            idWithExtra += "_";
            extraIdPartsLength++;

            var currentByte = stream.ReadByte();
            while (currentByte != '\0')
            {
                idWithExtra += (char)currentByte;
                extraIdPartsLength++;
                currentByte = stream.ReadByte();
            }

            stream.Position -= 1;
        }
        else
        {
            stream.Position = currentPos;
        }

        return (idWithExtra, extraIdPartsLength);
    }

    private static string getLocalizedText(
        int baseTextAddr,
        int nextOffset,
        int prevOffset,
        MemoryStream stream
    )
    {
        stream.Position = baseTextAddr + prevOffset;
        var strBytes = new byte[nextOffset - 1];
        stream.ReadExactly(strBytes, 0, strBytes.Length);
        return Encoding.UTF8.GetString(strBytes);
    }

    private static int getNextOffset(
        int extraIdPartsLength,
        MemoryStream stream,
        int distanceBetweenIds = 29
    )
    {
        stream.Position += distanceBetweenIds - extraIdPartsLength;
        var addrBytes = new byte[4];
        stream.ReadExactly(addrBytes, 0, addrBytes.Length);
        return BitConverter.ToInt32(addrBytes, 0);
    }

    private static MemoryStream openLocaFile(string filePath, int headerLength = 8)
    {
        if (
            !File.Exists(filePath)
            && !new FileInfo(filePath).Extension.Equals(
                ".loca",
                StringComparison.InvariantCultureIgnoreCase
            )
        )
        {
            throw new ArgumentException("The file is not a .loca file");
        }

        var fileBuffer = File.ReadAllBytes(filePath);
        var header = String.Join("", fileBuffer.Take(headerLength).Select(b => (char)b).ToArray());
        if (!header.StartsWith("LOCA", StringComparison.CurrentCulture))
        {
            throw new ArgumentException("The file is not a valid .loca file");
        }

        return new MemoryStream(fileBuffer);
    }
}
