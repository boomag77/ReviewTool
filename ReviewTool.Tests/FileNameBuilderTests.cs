using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ReviewTool.Tests;

[TestClass]
public sealed class FileNameBuilderTests
{
    private const string SourcePath = @"C:\images\source.jpg";

    [TestMethod]
    public void BuildReviewedFileName_EmptyOrZero_ReturnsAllZeros_MaxDigits3()
    {
        var maxDigits = 3;
        var expectedBase = new string('0', maxDigits);

        Assert.AreEqual(expectedBase + ".jpg", BuildWithNewBuilder(maxDigits, string.Empty));
        Assert.AreEqual(expectedBase + ".jpg", BuildWithNewBuilder(maxDigits, "0"));
        Assert.AreEqual(expectedBase + ".jpg", BuildWithNewBuilder(maxDigits, new string('0', maxDigits)));
        Assert.AreEqual(expectedBase + ".jpg", BuildWithNewBuilder(maxDigits, new string('0', maxDigits + 1)));
    }

    [TestMethod]
    public void BuildReviewedFileName_EmptyOrZero_ReturnsAllZeros_MaxDigits4()
    {
        var maxDigits = 4;
        var expectedBase = new string('0', maxDigits);

        Assert.AreEqual(expectedBase + ".jpg", BuildWithNewBuilder(maxDigits, string.Empty));
        Assert.AreEqual(expectedBase + ".jpg", BuildWithNewBuilder(maxDigits, "0"));
        Assert.AreEqual(expectedBase + ".jpg", BuildWithNewBuilder(maxDigits, " 0 "));
        Assert.AreEqual(expectedBase + ".jpg", BuildWithNewBuilder(maxDigits, new string('0', maxDigits)));
        Assert.AreEqual(expectedBase + ".jpg", BuildWithNewBuilder(maxDigits, new string('0', maxDigits + 1)));
    }

    [TestMethod]
    public void BuildReviewedFileName_NumericInput_NormalizesToThreeDigits_Until999()
    {
        var maxDigits = 4;
        var expectedBase = "001";

        Assert.AreEqual(expectedBase + ".jpg", BuildWithNewBuilder(maxDigits, "1"));
        Assert.AreEqual(expectedBase + ".jpg", BuildWithNewBuilder(maxDigits, "01"));
        Assert.AreEqual(expectedBase + ".jpg", BuildWithNewBuilder(maxDigits, "001"));
        Assert.AreEqual(expectedBase + ".jpg", BuildWithNewBuilder(maxDigits, "0001"));
        Assert.AreEqual(expectedBase + ".jpg", BuildWithNewBuilder(maxDigits, " 1 "));
        Assert.AreEqual("999.jpg", BuildWithNewBuilder(maxDigits, "999"));
        Assert.AreEqual("1000.jpg", BuildWithNewBuilder(maxDigits, "1000"));
        Assert.AreEqual("12345.jpg", BuildWithNewBuilder(maxDigits, "12345"));
    }

    [TestMethod]
    public void BuildReviewedFileName_NumericPrefixWithLetters_NormalizesLeadingDigits()
    {
        var maxDigits = 4;

        Assert.AreEqual(PadPrefix("12AB", 3) + ".jpg", BuildWithNewBuilder(maxDigits, "12AB"));
        Assert.AreEqual(PadPrefix("7Z", 3) + ".jpg", BuildWithNewBuilder(maxDigits, "7Z"));
        Assert.AreEqual("1000AB.jpg", BuildWithNewBuilder(maxDigits, "1000AB"));
        Assert.AreEqual("012ab.jpg", BuildWithNewBuilder(maxDigits, "12ab"));
    }

    [TestMethod]
    public void BuildReviewedFileName_FallbackWithLeadingUnderscore_PrefixesUnderscore()
    {
        var maxDigits = 3;
        Assert.AreEqual("_ab_12.jpg", BuildWithNewBuilder(maxDigits, "ab_12"));
        Assert.AreEqual("_ab-12.jpg", BuildWithNewBuilder(maxDigits, "ab-12"));
    }

    [TestMethod]
    public void BuildReviewedFileName_LettersOnly_UsesThreeLeadingZeros()
    {
        var maxDigits = 4;
        Assert.AreEqual("000AB.jpg", BuildWithNewBuilder(maxDigits, "AB"));
        Assert.AreEqual("000ab.jpg", BuildWithNewBuilder(maxDigits, "ab"));
        Assert.AreEqual("000Ab.jpg", BuildWithNewBuilder(maxDigits, "Ab"));
        Assert.AreEqual("000AB.jpg", BuildWithNewBuilder(maxDigits, " AB "));
    }

    [TestMethod]
    public void BuildReviewedFileName_AppendsSuffix_WhenNameAlreadyExists()
    {
        var maxDigits = 3;
        Assert.AreEqual("001A.jpg", BuildWithExistingNames(maxDigits, "001", "A"));
        Assert.AreEqual("001B.jpg", BuildWithExistingNames(maxDigits, "001", "B"));
    }

    [TestMethod]
    public void BuildReviewedFileName_AppendsSuffix_ZA_AfterZ()
    {
        var maxDigits = 3;
        Assert.AreEqual("001ZA.jpg", BuildWithExistingNames(maxDigits, "001", "ZA"));
    }

    [TestMethod]
    public void BuildReviewedFileName_AppendsSuffix_ZZA_AfterZZ()
    {
        var maxDigits = 3;
        Assert.AreEqual("001ZZA.jpg", BuildWithExistingNames(maxDigits, "001", "ZZA"));
    }

    [TestMethod]
    public void BuildReviewedFileName_AppendsSuffix_ZZZA_AfterZZZ()
    {
        var maxDigits = 3;
        Assert.AreEqual("001ZZZA.jpg", BuildWithExistingNames(maxDigits, "001", "ZZZA"));
    }

    [TestMethod]
    public void BuildReviewedFileName_AppendsSuffix_OnRepeatedCalls_WithSameBuilder()
    {
        var builder = new FileNameBuilder(3);
        builder.Reset(Array.Empty<string>());

        var first = builder.BuildReviewedFileName(SourcePath, "001".AsSpan(), out _);
        var second = builder.BuildReviewedFileName(SourcePath, "001".AsSpan(), out _);

        Assert.AreEqual("001.jpg", first);
        Assert.AreEqual("001A.jpg", second);
    }

    [TestMethod]
    public void BuildReviewedFileName_PreservesExtension()
    {
        var builder = new FileNameBuilder(3);
        builder.Reset(Array.Empty<string>());

        var result = builder.BuildReviewedFileName(@"C:\images\source.tif", "1".AsSpan(), out _);
        Assert.AreEqual("001.tif", result);
    }

    [TestMethod]
    public void BuildReviewedFileName_ResetTracksExistingAcrossExtensions()
    {
        var builder = new FileNameBuilder(3);
        builder.Reset(new[]
        {
            @"C:\existing\001.jpg",
            @"C:\existing\001.tif",
        });

        var result = builder.BuildReviewedFileName(@"C:\images\source.png", "1".AsSpan(), out _);
        Assert.AreEqual("001A.png", result);
    }

    [TestMethod]
    public void BuildReviewedFileName_WhitespaceInNumericPrefix_FallsBackToUnderscore()
    {
        var maxDigits = 3;
        Assert.AreEqual("012AB.jpg", BuildWithNewBuilder(maxDigits, "12 AB"));
    }

    [TestMethod]
    public void BuildReviewedFileName_HasPageNumber_False_ForEmptyOrZero()
    {
        var builder = new FileNameBuilder(3);
        builder.Reset(Array.Empty<string>());

        _ = builder.BuildReviewedFileName(SourcePath, "".AsSpan(), out var hasPageNumberEmpty);
        _ = builder.BuildReviewedFileName(SourcePath, "0".AsSpan(), out var hasPageNumberZero);

        Assert.IsFalse(hasPageNumberEmpty);
        Assert.IsFalse(hasPageNumberZero);
    }

    [TestMethod]
    public void BuildReviewedFileName_HasPageNumber_True_ForNonZero()
    {
        var builder = new FileNameBuilder(3);
        builder.Reset(Array.Empty<string>());

        _ = builder.BuildReviewedFileName(SourcePath, "1".AsSpan(), out var hasPageNumber);
        Assert.IsTrue(hasPageNumber);
    }

    private static string BuildWithNewBuilder(int maxDigits, string newName)
    {
        var builder = new FileNameBuilder(maxDigits);
        builder.Reset(Array.Empty<string>());
        return builder.BuildReviewedFileName(SourcePath, newName.AsSpan(), out _);
    }

    private static string BuildWithExistingNames(int maxDigits, string baseName, string expectedSuffix)
    {
        var existingBaseNames = new List<string> { baseName };
        foreach (var suffix in GetSuffixesBefore(expectedSuffix))
        {
            existingBaseNames.Add(baseName + suffix);
        }

        var existingPaths = existingBaseNames
            .Select(name => $@"C:\existing\{name}.jpg")
            .ToArray();

        var builder = new FileNameBuilder(maxDigits);
        builder.Reset(existingPaths);

        var result = builder.BuildReviewedFileName(SourcePath, baseName.AsSpan(), out _);
        return result;
    }

    private static IEnumerable<string> GetSuffixesBefore(string targetSuffix)
    {
        if (string.IsNullOrEmpty(targetSuffix))
        {
            yield break;
        }

        var current = "A";
        while (!string.Equals(current, targetSuffix, StringComparison.Ordinal))
        {
            yield return current;
            current = NextSuffix(current);
        }
    }

    private static string NextSuffix(string suffix)
    {
        if (string.IsNullOrEmpty(suffix))
        {
            return "A";
        }

        var lastChar = suffix[^1];
        if (lastChar < 'Z')
        {
            return suffix[..^1] + (char)(lastChar + 1);
        }

        return new string('Z', suffix.Length) + "A";
    }

    private static string PadPrefix(string name, int maxDigits)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return new string('0', maxDigits);
        }

        var span = name.AsSpan().Trim();
        var i = 0;
        while (i < span.Length && char.IsDigit(span[i]))
        {
            i++;
        }

        if (i == 0)
        {
            return span.ToString();
        }

        var prefix = span[..i].ToString().TrimStart('0');
        if (prefix.Length == 0)
        {
            prefix = "0";
        }

        var normalizedPrefix = prefix.PadLeft(maxDigits, '0');
        return normalizedPrefix + span[i..].ToString();
    }
}
