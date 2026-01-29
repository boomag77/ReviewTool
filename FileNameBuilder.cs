using System;
using System.Collections.Generic;
using System.IO;

namespace ReviewTool
{
    internal sealed class FileNameBuilder
    {

        private readonly int _maxDigitLength;
        private readonly HashSet<string> _usedBaseNames = new(StringComparer.OrdinalIgnoreCase);
        private readonly string emptyName;

        public FileNameBuilder(int maxDigits)
        {
            _maxDigitLength = maxDigits;
            Span<char> emptyNameSpan = stackalloc char[_maxDigitLength];
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

        private static bool HasOnlyDigits(ReadOnlySpan<char> s)
        {
            for (var i = 0; i < s.Length; i++)
            {
                if (!char.IsDigit(s[i]))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool HasOnlyLetters(ReadOnlySpan<char> s)
        {
            for (var i = 0; i < s.Length; i++)
            {
                if (!char.IsLetter(s[i]))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool TryGetNumericPrefix(ReadOnlySpan<char> s, out ReadOnlySpan<char> numPrefix, out int prefixLen)
        {
            numPrefix = ReadOnlySpan<char>.Empty;
            prefixLen = 0;
            if (s.IsEmpty) return false;

            int i = 0;

            while (i < s.Length && char.IsDigit(s[i]))
                i++;

            prefixLen = i;
            if (prefixLen == 0) return false;
            numPrefix = s.Slice(0, prefixLen);

            return true;
        }

        private static bool TryGetLetterSuffix(ReadOnlySpan<char> s, out string suffix)
        {
            suffix = string.Empty;
            if (s.IsEmpty) return false;
            int i = s.Length - 1;
            while (i >= 0 && char.IsLetter(s[i]))
                i--;
            int suffixLen = s.Length - 1 - i;
            if (suffixLen == 0) return false;
            suffix = s.Slice(i + 1, suffixLen).ToString();
            return true;
        }

        // assume that the input is valid: no extension, only digits or empty
        public string BuildReviewedFileName(ReadOnlySpan<char> sourcePath, ReadOnlySpan<char> newName, out bool hasPageNumber)
        {
            ReadOnlySpan<char> ext = Path.GetExtension(sourcePath);
            hasPageNumber = true;
            if (newName.IsEmpty)
            {
                hasPageNumber = false;
                var uniqueEmptyName = GetUniqueBaseName(emptyName);
                return string.Concat(uniqueEmptyName, ext);
            }
            ReadOnlySpan<char> trimmedNewNameSpan = newName.Trim();
            bool newNameHasOnlyDigits = HasOnlyDigits(trimmedNewNameSpan);
            bool newNameHasOnlyLetters = HasOnlyLetters(trimmedNewNameSpan);
            var reviewedName = string.Empty;

            if (newNameHasOnlyDigits)
            {
                var normalizedNumber = NormalizeNumericName(trimmedNewNameSpan);
                reviewedName = GetUniqueBaseName(normalizedNumber);
            }
            else if (newNameHasOnlyLetters)
            {
                var normalizedString = string.Concat(emptyName, trimmedNewNameSpan);
                reviewedName = GetUniqueBaseName(normalizedString);
            }
                
            else if (TryGetNumericPrefix(trimmedNewNameSpan, out ReadOnlySpan<char> numericPrefix, out var numericPrefixLength) &&
               TryGetLetterSuffix(trimmedNewNameSpan.Slice(numericPrefixLength), out string letterSuffix))
            {

                var normalizedNumericPrefix = NormalizeNumericName(numericPrefix);
                var combinedName = string.Concat(normalizedNumericPrefix, letterSuffix);
                reviewedName = GetUniqueBaseName(combinedName);
            }
            else
            {
                var unspecifiedName = string.Concat("_", trimmedNewNameSpan);
                reviewedName = GetUniqueBaseName(unspecifiedName);
            }
                _usedBaseNames.Add(reviewedName);

            return string.Concat(reviewedName, ext);
        }

        private string NormalizeNumericName(ReadOnlySpan<char> numberString)
        {
            var nededPadding = _maxDigitLength - numberString.Length;
            return nededPadding > 0
                ? string.Concat(new string('0', nededPadding), numberString.ToString())
                : numberString.ToString();
        }

        private string GetUniqueBaseName(string name)
        {
            if (!_usedBaseNames.Contains(name))
            {
                return name;
            }

            var suffix = "A";
            while (_usedBaseNames.Contains(string.Concat(name, suffix)))
            {
                suffix = IncrementLetterSuffix(suffix);
            }

            return string.Concat(name, suffix);
        }

    }
}

