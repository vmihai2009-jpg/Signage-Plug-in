using System.Linq;
using BGSignage.Core.Geometry;
using BGSignage.Core.Layout;
using BGSignage.Core.Model;
using Xunit;

namespace BGSignage.Tests
{
    public class MaterialTests
    {
        [Fact]
        public void Each_layer_has_its_own_material_falling_back_to_the_sign_default()
        {
            var d = Presets.Create("Floor directory (numbers + rules)");
            var a = d.Frames[0]; var b = d.Frames[1];
            Assert.Same(d.Build.LetterMaterial, d.MaterialFor(a.Id, MaterialSlot.Letters));
            d.SetMaterial(a.Id, MaterialSlot.Letters, new MaterialRef("Brass", 200, 170, 120));
            Assert.Equal("Brass", d.MaterialFor(a.Id, MaterialSlot.Letters).Name);
            Assert.Same(d.Build.LetterMaterial, d.MaterialFor(b.Id, MaterialSlot.Letters));
            var back = SignSerializer.FromJson(SignSerializer.ToJson(d));
            Assert.Equal("Brass", back.MaterialFor(a.Id, MaterialSlot.Letters).Name);
        }

        [Fact]
        public void Material_parameter_names_are_unique_and_cover_every_piece()
        {
            foreach (var name in Presets.Names)
            {
                var d = Presets.Create(name);
                d.Frames.ForEach(f => f.Name = "Same");
                d.Rules.ForEach(r => r.Name = "Same");
                var names = d.MaterialParameterNames();
                Assert.Equal(names.Count, names.Values.Distinct().Count());
                var g = SignBuilder.Build(d, new BoxFont());
                Assert.All(g.Pieces, p => Assert.True(names.ContainsKey((p.ElementId, p.Material)), $"{name}: {p.Label}"));
            }
        }

        [Fact]
        public void Symbols_build_and_their_lines_scale_with_them()
        {
            foreach (var n in Symbols.Names)
            {
                var d = new SignDocument();
                var s = Symbols.Create(n, 100);
                d.Graphics.Add(s);
                var small = SignBuilder.Build(d.Normalise(), new BoxFont());
                Assert.NotEmpty(small.Pieces);
                s.ScalePercent = 200;
                var big = SignBuilder.Build(d, new BoxFont());
                Assert.Equal(small.ContentBounds.Width * 2, big.ContentBounds.Width, 2);
                Assert.Equal(small.ContentBounds.Height * 2, big.ContentBounds.Height, 2);
            }
        }

        [Theory]
        [InlineData("Person", 2)]       // head (a ring, with its hole) + body
        [InlineData("Arrow right", 1)]
        [InlineData("Stairs", 1)]
        [InlineData("Toilets", 4)]      // two heads, two bodies
        public void Symbols_come_out_as_merged_solid_shapes(string name, int shapes)
        {
            var d = new SignDocument();
            d.Graphics.Add(Symbols.Create(name, 100));
            var g = SignBuilder.Build(d.Normalise(), new BoxFont());
            Assert.Equal(shapes, g.Pieces.Count);
            // Merged shapes never overlap, so Revit shows no edges where strokes cross.
            for (int i = 0; i < g.Pieces.Count; i++)
                for (int j = i + 1; j < g.Pieces.Count; j++)
                    Assert.False(Polygons.Contains(g.Pieces[j].Loops[0], g.Pieces[i].Loops[0][0]) && !g.Pieces[j].Loops.Skip(1).Any(h => Polygons.Contains(h, g.Pieces[i].Loops[0][0])),
                        $"{name}: shape {i} lies inside shape {j}");
            if (name == "Person")
                Assert.Contains(g.Pieces, p => p.Loops.Count == 2); // the head keeps its hole
        }
    }
}
