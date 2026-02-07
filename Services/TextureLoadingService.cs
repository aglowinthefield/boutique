using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using HelixToolkit.Wpf.SharpDX;
using Pfim;
using Serilog;

namespace Boutique.Services;

public enum AlphaType
{
  FullyOpaque,
  AlphaTest,
  TrueTransparency,
  ProblematicLowAlpha
}

public static class TextureLoadingService
{
  private static readonly Dictionary<string, (TextureModel? Texture, bool NeedsTransparency)> TextureCache =
    new(StringComparer.OrdinalIgnoreCase);

  private static readonly object TextureCacheLock = new();

  public static (TextureModel? Texture, bool NeedsTransparency) LoadDdsTexture(string texturePath)
  {
    lock (TextureCacheLock)
    {
      if (TextureCache.TryGetValue(texturePath, out var cached))
      {
        return cached;
      }
    }

    var result = LoadDdsTextureCore(texturePath);

    lock (TextureCacheLock)
    {
      TextureCache[texturePath] = result;
    }

    return result;
  }

  public static void ClearCache()
  {
    lock (TextureCacheLock)
    {
      TextureCache.Clear();
    }
  }

  private static (TextureModel? Texture, bool NeedsTransparency) LoadDdsTextureCore(string texturePath)
  {
    try
    {
      using var image = Pfimage.FromFile(texturePath);

      if (image.Format != ImageFormat.Rgba32)
      {
        return (new TextureModel(texturePath), false);
      }

      var alphaType = AnalyzeAlphaChannel(image.Data, image.Width, image.Height, image.Stride);

      switch (alphaType)
      {
        case AlphaType.FullyOpaque:
          return (new TextureModel(texturePath), false);

        case AlphaType.TrueTransparency:
          return (new TextureModel(texturePath), true);

        case AlphaType.AlphaTest:
          var thresholdedData = ApplyAlphaThreshold(image.Data, image.Width, image.Height, image.Stride);
          var texture = CreateTextureFromPixels(thresholdedData, image.Width, image.Height, image.Stride);
          return (texture, false);

        case AlphaType.ProblematicLowAlpha:
        default:
          var opaqueData = ForceOpaqueAlpha(image.Data, image.Width, image.Height, image.Stride);
          var opaqueTexture = CreateTextureFromPixels(opaqueData, image.Width, image.Height, image.Stride);
          return (opaqueTexture, false);
      }
    }
    catch (Exception ex)
    {
      Log.Warning(ex, "Failed to load DDS texture with Pfim: {TexturePath}", texturePath);
      return (null, false);
    }
  }

  private static AlphaType AnalyzeAlphaChannel(byte[] data, int width, int height, int stride)
  {
    var sampleCount = 0;
    var opaqueCount = 0;
    var transparentCount = 0;
    var semiTransparentCount = 0;
    var lowAlphaCount = 0;

    var stepX = Math.Max(1, width / 32);
    var stepY = Math.Max(1, height / 32);

    for (var y = 0; y < height; y += stepY)
    {
      var rowStart = y * stride;
      for (var x = 0; x < width; x += stepX)
      {
        var alphaIndex = rowStart + x * 4 + 3;
        if (alphaIndex >= data.Length)
        {
          continue;
        }

        var alpha = data[alphaIndex];
        sampleCount++;

        switch (alpha)
        {
          case 255:
            opaqueCount++;
            break;
          case 0:
            transparentCount++;
            break;
          case < 64:
            lowAlphaCount++;
            break;
          default:
            semiTransparentCount++;
            break;
        }
      }
    }

    if (sampleCount == 0)
    {
      return AlphaType.FullyOpaque;
    }

    var opaqueRatio = (float)opaqueCount / sampleCount;
    var transparentRatio = (float)transparentCount / sampleCount;
    var semiTransparentRatio = (float)semiTransparentCount / sampleCount;
    var lowAlphaRatio = (float)lowAlphaCount / sampleCount;

    if (opaqueRatio > 0.95f)
    {
      return AlphaType.FullyOpaque;
    }

    if (semiTransparentRatio > 0.1f)
    {
      return AlphaType.TrueTransparency;
    }

    if (opaqueRatio + transparentRatio > 0.9f)
    {
      return AlphaType.AlphaTest;
    }

    if (lowAlphaRatio > 0.5f)
    {
      return AlphaType.ProblematicLowAlpha;
    }

    return AlphaType.TrueTransparency;
  }

  private static TextureModel? CreateTextureFromPixels(byte[] data, int width, int height, int stride)
  {
    var pinnedData = GCHandle.Alloc(data, GCHandleType.Pinned);
    try
    {
      var bitmapSource = BitmapSource.Create(
        width,
        height,
        96,
        96,
        PixelFormats.Bgra32,
        null,
        pinnedData.AddrOfPinnedObject(),
        data.Length,
        stride);

      bitmapSource.Freeze();

      using var memoryStream = new MemoryStream();
      var encoder = new BmpBitmapEncoder();
      encoder.Frames.Add(BitmapFrame.Create(bitmapSource));
      encoder.Save(memoryStream);

      return new TextureModel(new MemoryStream(memoryStream.ToArray()));
    }
    finally
    {
      pinnedData.Free();
    }
  }

  private static byte[] ApplyAlphaThreshold(byte[] data, int width, int height, int stride)
  {
    var result = new byte[data.Length];
    Buffer.BlockCopy(data, 0, result, 0, data.Length);

    var span = result.AsSpan();
    for (var y = 0; y < height; y++)
    {
      var rowStart = y * stride;
      var rowEnd = Math.Min(rowStart + width * 4, span.Length);
      for (var i = rowStart + 3; i < rowEnd; i += 4)
      {
        span[i] = span[i] >= 128 ? (byte)255 : (byte)0;
      }
    }

    return result;
  }

  private static byte[] ForceOpaqueAlpha(byte[] data, int width, int height, int stride)
  {
    var result = new byte[data.Length];
    Buffer.BlockCopy(data, 0, result, 0, data.Length);

    var span = result.AsSpan();
    for (var y = 0; y < height; y++)
    {
      var rowStart = y * stride;
      var rowEnd = Math.Min(rowStart + width * 4, span.Length);
      for (var i = rowStart + 3; i < rowEnd; i += 4)
      {
        span[i] = 255;
      }
    }

    return result;
  }
}
