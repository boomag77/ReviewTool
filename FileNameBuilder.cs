using System.IO;    

namespace ReviewTool
{
    internal sealed class FileNameBuilder
    {

        private const int maxDigitLength = 3;
        private readonly string emptyName;

        FileNameBuilder()
        {
            LastEmptySuffix = string.Empty;
            LastLetterSuffix = string.Empty;
            LastNumFileName = string.Empty;
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

        private string LastNumFileName
        {
            get => field;
            set => field = value;
        }

        private string LastLetterSuffix
        {
            get => field;
            set => field = value;
        }

        private string LastEmptySuffix
        {
            get => field;
            set => field = value;
        }

        // assume that the input is valid: no extension, only digits or empty
        public string BuildReviewedFileName(string sourcePath, string newName)
        {
            var resultName = string.Empty;
            var ext = Path.GetExtension(sourcePath);
            if (string.IsNullOrWhiteSpace(newName))
            {
                if (LastEmptySuffix == string.Empty)
                {
                    LastEmptySuffix = IncrementLetterSuffix(LastEmptySuffix);
                    resultName = string.Concat(emptyName, LastEmptySuffix, ext);
                }
                else
                {
                    var newLetterSuffix = IncrementLetterSuffix(LastEmptySuffix);
                    var result = string.Concat(emptyName, newLetterSuffix, ext);
                    LastEmptySuffix = newLetterSuffix;
                    return result;
                }
            }

            Span<char> newNameSpan = stackalloc char[newName.Length];
            newName.AsSpan().CopyTo(newNameSpan);
            if (newNameSpan.TrimStart('0').Length == 0)
            {
                // all zeros
                if (LastNumFileName == string.Empty)
                {
                    LastNumFileName = newName;
                    resultName = string.Concat(newName, ext);
                }
                else
                {
                    int lastNum = int.Parse(LastNumFileName);
                    int newNum = lastNum + 1;
                    var newNumStr = newNum.ToString().PadLeft(newName.Length, '0');
                    LastNumFileName = newNumStr;
                    return string.Concat(newNumStr, ext);
                }
            }
            else
            {
                // has non-zero characters
                if (string.Compare(newName, LastNumFileName, StringComparison.Ordinal) > 0)
                {
                    LastNumFileName = newName;
                    resultName = string.Concat(newName, ext);
                }
                else
                {
                    int lastNum = int.Parse(LastNumFileName);
                    int newNum = lastNum + 1;
                    var newNumStr = newNum.ToString().PadLeft(newName.Length, '0');
                    LastNumFileName = newNumStr;
                    return string.Concat(newNumStr, ext);
                }
            }

            return resultName;
        }

    }
}
