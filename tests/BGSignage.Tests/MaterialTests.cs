using System.Linq;
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
                var line = small.Pieces.First(p => p.Label.EndsWith("line"));
                var line2 = big.Pieces.First(p => p.Label.EndsWith("line"));
                Assert.Equal(line.Bounds.Width * 2, line2.Bounds.Width, 3);
            }
        }
    }
}
