using System;
using System.Collections.Generic;
using System.IO;

namespace ReviewTool
{
    internal sealed class FileNameBuilder
    {

        private const int maxDigitLength = 3;
        private readonly HashSet<string> _usedBaseNames = new(StringComparer.OrdinalIgnoreCase);
        private readonly string emptyName;

        public FileNameBuilder()
        {
            Span<char> emptyNameSpan = stackalloc char[maxDigitLength];
            emptyNameSpan.Fill('0');
            emptyName = new string(emptyNameSpan);
        }

        private static string IncrementLetterSuffix(string suffix)
        {
            if (string.IsNullOrEmpty(suffix))
            {
                return "A";
            }
            char lastChar = suffix[^1];
            if (lastChar == 'Z')
            {
                return IncrementLetterSuffix(suffix[..^1]) + 'A';
            }
            else
            {
                return suffix[..^1] + (char)(lastChar + 1);
            }
        }

        public void Reset(IEnumerable<string> existingFilePaths)
        {
            _usedBaseNames.Clear();
            foreach (var path in existingFilePaths)
            {
                var baseName = Path.GetFileNameWithoutExtension(path);
                if (string.IsNullOrWhiteSpace(baseName))
                {
                    continue;
                }
                _usedBaseNames.Add(baseName);
            }
        }

        // assume that the input is valid: no extension, only digits or empty
        public string BuildReviewedFileName(string sourcePath, string newName, out bool hasPageNumber)
        {
            var ext = Path.GetExtension(sourcePath);
            var normalized = NormalizeNumericName(newName);
            var baseName = GetUniqueBaseName(normalized);
            _usedBaseNames.Add(baseName);
            hasPageNumber = !string.IsNullOrEmpty(newName);
            return string.Concat(baseName, ext);
        }

        private string NormalizeNumericName(string newName)
        {
            if (string.IsNullOrWhiteSpace(newName))
            {
                return emptyName;
            }

            var trimmed = newName.Trim();
            if (!int.TryParse(trimmed, out var number))
            {
                return trimmed.PadLeft(maxDigitLength, '0');
            }

            return number.ToString($"D{maxDigitLength}");
        }

        private string GetUniqueBaseName(string normalized)
        {
            if (!_usedBaseNames.Contains(normalized))
            {
                return normalized;
            }

            var suffix = "A";
            while (_usedBaseNames.Contains(string.Concat(normalized, suffix)))
            {
                suffix = IncrementLetterSuffix(suffix);
            }

            return string.Concat(normalized, suffix);
        }

    }
}

