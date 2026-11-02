using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using BGSignage.Core.Import;
using BGSignage.Core.Layout;
using BGSignage.Core.Model;
using Xunit;

namespace BGSignage.Tests
{
    public class ImportTests
    {
        /// <summary>A minimal PDF like Illustrator writes: a compressed page content stream drawing inside a form.</summary>
        static byte[] Pdf(string content, bool compress = true)
        {
            byte[] data = Encoding.ASCII.GetBytes(content);
            string filter = "";
            if (compress)
            {
                using (var ms = new MemoryStream())
                {
                    using (var z = new ZLibStream(ms, CompressionLevel.Optimal, true)) z.Write(data, 0, data.Length);
                    data = ms.ToArray();
                }
                filter = "/Filter /FlateDecode ";
            }
            var sb = new MemoryStream();
            void W(string s) { var b = Encoding.ASCII.GetBytes(s); sb.Write(b, 0, b.Length); }
            W("%PDF-1.6\n%âã\n");
            W("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
            W("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");
            W("3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 600 600] /Contents 4 0 R /Resources << /XObject << /Fm0 5 0 R >> >> >>\nendobj\n");
            W($"4 0 obj\n<< {filter}/Length {data.Length} >>\nstream\n");
            sb.Write(data, 0, data.Length);
            W("\nendstream\nendobj\n");
            var form = Encoding.ASCII.GetBytes("0 0 0 rg 0 0 m 72 0 l 72 72 l h f\n");
            W($"5 0 obj\n<< /Type /XObject /Subtype /Form /BBox [0 0 100 100] /Matrix [1 0 0 1 200 0] /Length {form.Length} >>\nstream\n");
            sb.Write(form, 0, form.Length);
            W("\nendstream\nendobj\ntrailer\n<< /Root 1 0 R >>\n%%EOF\n");
            return sb.ToArray();
        }

        [Fact]
        public void Reads_filled_shapes_holes_strokes_and_forms_in_millimetres()
        {
            string content =
                "q 1 0 0 1 0 0 cm 0 0 0 rg\n" +
                "0 0 72 72 re 18 18 36 36 re f*\n" +          // 25.4 mm square with a hole
                "0 0 0 RG 2 w 100 0 m 172 0 l S\n" +           // a 25.4 mm line, 2 pt wide
                "1 1 1 rg -100 -100 1000 1000 re f\n" +        // white background: ignored
                "/Fm0 Do Q\n";
            var paths = PdfVectorReader.Read(Pdf(content));
            var closed = paths.Where(p => p.Closed && p.StrokeMm == 0).ToList();
            var open = paths.Where(p => !p.Closed).ToList();
            Assert.Equal(3, closed.Count); // square, hole, triangle from the form
            Assert.Single(open);
            Assert.Equal(25.4, open[0].Points[1].X - open[0].Points[0].X, 3);
            Assert.Equal(2 * 25.4 / 72, open[0].StrokeMm, 3);
            var tri = closed.Single(p => p.Points.Length == 3);
            Assert.Equal(200 * 25.4 / 72, tri.Points[0].X, 3);
            // y is flipped to point down
            Assert.True(closed.First().Points.Any(p => p.Y < -1));
        }

        [Fact]
        public void Graphic_becomes_solids_with_holes_on_the_sign()
        {
            var paths = PdfVectorReader.Read(Pdf("0 0 72 72 re 18 18 36 36 re f\n", compress: false));
            var g = GraphicElement.FromPaths("Logo", "logo.ai", paths.Select(p => (p.Points, p.Closed)));
            Assert.Equal(25.4, g.NativeWidth, 3);
            g.X = 100; g.Y = 50; g.ScalePercent = 200;
            var doc = new SignDocument();
            doc.Graphics.Add(g);
            var geom = SignBuilder.Build(doc, new BoxFont());
            var piece = Assert.Single(geom.Pieces);
            Assert.Equal(2, piece.Loops.Count);
            Assert.Equal(100, piece.Bounds.MinX, 3);
            Assert.Equal(100 + 50.8, piece.Bounds.MaxX, 3);
        }

        [Fact]
        public void Files_without_a_pdf_copy_explain_what_to_do()
        {
            var ex = Assert.Throws<InvalidDataException>(() => PdfVectorReader.Read(Encoding.ASCII.GetBytes("%!PS-Adobe-3.0 old illustrator")));
            Assert.Contains("PDF Compatible", ex.Message);
        }
    }
}
