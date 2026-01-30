using BitMiracle.LibTiff.Classic;
using System.Diagnostics;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ReviewTool;

internal class TiffReader
{
    private struct TiffImageInfo
    {
        public int Width;
        public int Height;
        public int Compression;
        public int PhotoMetric;
        public int FillOrder;
        public int SamplesPerPixel;
        public int BitsPerSample;
        public bool IsRawCompressed;
        public byte[] Data;
        public byte[][] RawStrips;
        public int RowsPerStripOriginal;
    }

    private static int GetIntTag(Tiff tiff, TiffTag tag, int defaultValue = 0)
    {
        FieldValue[]? v = tiff.GetField(tag);
        return v != null ? v[0].ToInt() : defaultValue;
    }

    private static byte[] ExtractDecodedPixelsByStrip(Tiff image, int width, int height)
    {
        int stripsCount = image.NumberOfStrips();
        using var ms = new MemoryStream();

        for (int strip = 0; strip < stripsCount; strip++)
        {
            int stripSize = (int)image.StripSize();
            byte[] stripBuffer = new byte[stripSize];
            try
            {
                int read = image.ReadEncodedStrip(strip, stripBuffer, 0, stripSize);
                if (read > 0)
                    ms.Write(stripBuffer, 0, read);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error reading strip {strip}: {ex.Message}");
                return Array.Empty<byte>();
            }
        }

        return ms.ToArray();
    }

    private static async Task<TiffImageInfo?> ReadTiff(string filePath)
    {
        return await Task.Run<TiffImageInfo?>(() =>
        {
            try
            {
                using (Tiff tiff = Tiff.Open(filePath, "r"))
                {
                    if (tiff == null)
                    {
                        Debug.WriteLine("Could not open TIFF");
                        return null;
                    }

                    int width = GetIntTag(tiff, TiffTag.IMAGEWIDTH);
                    int height = GetIntTag(tiff, TiffTag.IMAGELENGTH);
                    if (width <= 0 || height <= 0)
                    {
                        Debug.WriteLine($"Invalid image size: width={width}, height={height}");
                        return null;
                    }

                    int samplesPerPixel = GetIntTag(tiff, TiffTag.SAMPLESPERPIXEL, 1);
                    int bitsPerSample = GetIntTag(tiff, TiffTag.BITSPERSAMPLE, 8);
                    int photoMetric = GetIntTag(tiff, TiffTag.PHOTOMETRIC, (int)Photometric.MINISBLACK);
                    int fillOrder = GetIntTag(tiff, TiffTag.FILLORDER, (int)FillOrder.MSB2LSB);
                    int planarConfig = GetIntTag(tiff, TiffTag.PLANARCONFIG, (int)PlanarConfig.CONTIG);
                    if (planarConfig != (int)PlanarConfig.CONTIG)
                    {
                        Debug.WriteLine("Unsupported planar configuration (not CONTIG)");
                        return null;
                    }

                    int compression = GetIntTag(tiff, TiffTag.COMPRESSION, (int)Compression.NONE);

                    byte[] data = Array.Empty<byte>();
                    bool isRaw = false;

                    switch (compression)
                    {
                        case (int)Compression.NONE:
                        case (int)Compression.LZW:
                        case (int)Compression.ADOBE_DEFLATE:
                        case (int)Compression.PACKBITS:
                        case (int)Compression.JPEG:
                            data = ExtractDecodedPixelsByStrip(tiff, width, height);
                            compression = (int)Compression.NONE;
                            isRaw = false;
                            break;

                        case (int)Compression.CCITTFAX3:
                        case (int)Compression.CCITTFAX4:
                            data = ExtractDecodedPixelsByStrip(tiff, width, height);
                            compression = (int)Compression.NONE;
                            isRaw = false;
                            break;
                        default:
                            Debug.WriteLine($"Other compression: {compression}");
                            break;
                    }

                    return new TiffImageInfo
                    {
                        Width = width,
                        Height = height,
                        Compression = compression,
                        FillOrder = fillOrder,
                        PhotoMetric = photoMetric,
                        SamplesPerPixel = samplesPerPixel,
                        BitsPerSample = bitsPerSample,
                        IsRawCompressed = isRaw,
                        Data = data,
                        RawStrips = Array.Empty<byte[]>(),
                        RowsPerStripOriginal = height
                    };
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error reading TIFF: {ex.Message}");
                return null;
            }
        });
    }

    private static MemoryStream TiffMemoryStream(TiffImageInfo tiffImageInfo)
    {
        MemoryStream resultStream;
        using (var temp = new MemoryStream())
        {
            using (var output = Tiff.ClientOpen("in-memory", "w", temp, new TiffStream()))
            {
                if (output == null)
                {
                    Debug.WriteLine("Could not open output TIFF");
                    return new MemoryStream();
                }

                output.SetField(TiffTag.IMAGEWIDTH, tiffImageInfo.Width);
                output.SetField(TiffTag.IMAGELENGTH, tiffImageInfo.Height);
                output.SetField(TiffTag.COMPRESSION, tiffImageInfo.Compression);
                output.SetField(TiffTag.SAMPLESPERPIXEL, tiffImageInfo.SamplesPerPixel);
                output.SetField(TiffTag.BITSPERSAMPLE, tiffImageInfo.BitsPerSample);
                output.SetField(TiffTag.PHOTOMETRIC, tiffImageInfo.PhotoMetric);
                output.SetField(TiffTag.PLANARCONFIG, (int)PlanarConfig.CONTIG);
                output.SetField(TiffTag.FILLORDER, tiffImageInfo.FillOrder);
                output.SetField(TiffTag.ROWSPERSTRIP, tiffImageInfo.Height);

                output.WriteEncodedStrip(0, tiffImageInfo.Data, 0, tiffImageInfo.Data.Length);
                output.WriteDirectory();
                output.Flush();
            }
            resultStream = new MemoryStream(temp.ToArray());
        }
        resultStream.Position = 0;
        return resultStream;
    }

    public static async Task<ImageSource?> LoadImageSourceFromTiff(string filePath)
    {
        var tiffInfo = await ReadTiff(filePath);
        if (tiffInfo == null)
            return null;
        var rawData = tiffInfo.Value.Data;
        if (rawData.Length == 0)
            return null;

        using var tiffStream = TiffMemoryStream(tiffInfo.Value);
        return CreateImageSource(tiffStream);
    }

    private static ImageSource? CreateImageSource(MemoryStream tiffStream)
    {
        try
        {
            tiffStream.Position = 0;
            var decoder = new TiffBitmapDecoder(tiffStream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var frame = decoder.Frames[0];
            var copy = new WriteableBitmap(frame);
            copy.Freeze();
            return copy;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to decode TIFF: {ex.Message}");
            return null;
        }
    }
}
