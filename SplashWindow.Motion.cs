namespace GlitchStich;

public partial class SplashWindow
{
    private readonly record struct IntroMotion(double X, double Y, double Z, double Yaw, double Pitch, double Roll);
    private static IntroMotion SampleIntro(double t)
    {
        static double Heading(double x, double z) => Math.Atan2(x, z) * 180 / Math.PI;
        if (t < .8)
        {
            var u = Math.Clamp(t / .8, 0, 1);
            return new(Lerp(-25, -20, u), 0, Lerp(-25, -18, u), Heading(5, 7), 0, 0);
        }
        if (t < 1.7)
        {
            var u = (t - .8) / .9;
            var x = Hermite(-20, -12, 5 / .8, 1.4 / .3, .9, u);
            var z = Hermite(-18, -6, 7 / .8, 1.8 / .3, .9, u);
            return new(x, 4 * 4.3 * u * (1 - u), z,
                Heading(HermiteVelocity(-20, -12, 5 / .8, 1.4 / .3, .9, u), HermiteVelocity(-18, -6, 7 / .8, 1.8 / .3, .9, u)), 360 * Smooth(u), 0);
        }
        if (t < 2.0)
        {
            var u = (t - 1.7) / .3;
            return new(Lerp(-12, -10.6, u), 0, Lerp(-6, -4.2, u), Heading(1.4, 1.8), 360, 0);
        }
        var p = Math.Clamp((t - 2) / 1.25, 0, 1);
        var directionAt = Math.Min(p, .999);
        var yaw = Heading(HermiteVelocity(-10.6, -4, 1.4 / .3, 0, 1.25, directionAt), HermiteVelocity(-4.2, 1, 1.8 / .3, 0, 1.25, directionAt));
        if (t < 3.25)
            return new(Hermite(-10.6, -4, 1.4 / .3, 0, 1.25, p), 4 * 3.7 * p * (1 - p), Hermite(-4.2, 1, 1.8 / .3, 0, 1.25, p), yaw, 360 + 22 * Math.Sin(Math.PI * p), 360 * Smooth(p));
        var settle = Smooth(Math.Clamp((t - 3.4) / .45, 0, 1));
        return new(-4, 0, 1, Lerp(yaw, 132, settle), 360, 360);
    }
}

