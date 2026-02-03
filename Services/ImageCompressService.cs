using System.IO;
using System.Threading.Tasks;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Formats.Jpeg;

namespace Smartsam.Services
{
    public class ImageCompressService
    {
        public async Task<byte[]> CompressAsync(Stream inputStream)
        {
            using (var image = await Image.LoadAsync(inputStream))
            {
                int maxWidth = 1280;
                int maxHeight = 1280;

                image.Mutate(x => x.Resize(new ResizeOptions
                {
                    Mode = ResizeMode.Max,
                    Size = new Size(maxWidth, maxHeight)
                }));

                var encoder = new JpegEncoder
                {
                    Quality = 45   // nén mạnh giống Zalo
                };

                using (var ms = new MemoryStream())
                {
                    await image.SaveAsJpegAsync(ms, encoder);
                    return ms.ToArray();
                }
            }
        }
    }
}