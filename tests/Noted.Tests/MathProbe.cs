using WpfMath.Parsers;
using Xunit.Abstractions;

namespace Noted.Tests;

public class MathProbe(ITestOutputHelper output)
{
    [Fact]
    public void ProbeEnvironments()
    {
        var parser = new TexFormulaParser();
        string[] cases =
        {
            @"\begin{matrix} 1 & 2 \ 3 & 4 \end{matrix}",
            @"\begin{pmatrix} 1 & 2 \ 3 & 4 \end{pmatrix}",
            @"\begin{bmatrix} 1 & 2 \ 3 & 4 \end{bmatrix}",
            @"\begin{Bmatrix} 1 & 2 \ 3 & 4 \end{Bmatrix}",
            @"\begin{vmatrix} 1 & 2 \ 3 & 4 \end{vmatrix}",
            @"\begin{Vmatrix} 1 & 2 \ 3 & 4 \end{Vmatrix}",
            @"\begin{aligned} a &= b + c \ x &= y - z \end{aligned}",
            @"\begin{cases} a & x>0 \ b & x<0 \end{cases}",
            @"\left[\begin{matrix} 1 & 2 \ 3 & 4 \end{matrix}\right]",
        };
        foreach (var c in cases)
        {
            string result;
            try { parser.Parse(c); result = "OK"; }
            catch (Exception ex) { result = ex.GetType().Name + ": " + ex.Message; }
            output.WriteLine($"{result,-40} | {c}");
        }
    }
}
