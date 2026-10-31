using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace GlitchStich;

public partial class SplashWindow
{
    private static readonly Rect3D VaultObstacle = new(-16.05, -2.724, -11.55, 2.10, 1.60, 2.10);
    private static readonly Rect3D ProjectionWall = new(-3.4, -3.1, -5.25, 11, 10, .5);

    private static void BuildPlatformCourse(Model3DGroup scene)
    {
        // Separate elevated decks leave a real visible gap, rather than a painted
        // gap on a continuous floor. Nothing from the projection wall is here.
        Box(scene, -8, -12, -9, 75, .2, 75, Surface("#030711", "#132738", .02));
        CourseDeck(scene, new(-28, 0, -29), new(-10, 0, -3.5), 6, .85);
        Box(scene, -4, -3.15, 1, 6.6, .85, 6.4, Armor);
        for (var i = 0; i < 5; i++)
        {
            Box(scene, -6.6 + i * 1.3, -2.716, 1, .018, .018, 6.2, Surface("#0C2536", "#247E91", .3));
            Box(scene, -4, -2.716, -1.8 + i * 1.3, 6.3, .018, .018, Surface("#182036", "#6A3A82", .25));
        }
        Box(scene, -4, -2.69, -2.17, 6.6, .055, .055, Cyan);
        Box(scene, -4, -2.69, 4.17, 6.6, .055, .055, Pink);
        Box(scene, -7.27, -2.69, 1, .055, .055, 6.4, Cyan);
        Box(scene, -.73, -2.69, 1, .055, .055, 6.4, Pink);
        foreach (var x in new[] { -6.3, -1.7 }) Box(scene, x, -7.2, 1, .55, 8, .55, Dark);
        var crate = new Model3DGroup { Transform = new TranslateTransform3D(-1, 0, -1.5) };
        BevelBox(crate, -14, -1.924, -9, 2.1, 1.6, 2.1, .10, Surface("#423127", "#B38963", .12));
        foreach (var x in new[] { -15.03, -12.97 })
        {
            Box(crate, x, -1.924, -9, .05, 1.58, 2.12, Amber);
            Box(crate, x, -1.10, -9, .065, .045, 2.12, Amber);
        }
        foreach (var z in new[] { -10.03, -7.97 })
        {
            Box(crate, -14, -1.10, z, 2.1, .045, .065, Amber);
            Tube(crate, new(-14.75, -2.55, z), new(-13.25, -1.30, z), .045, Amber);
            Tube(crate, new(-14.75, -1.30, z), new(-13.25, -2.55, z), .045, Chrome);
        }
        scene.Children.Add(crate);
        // Launch-edge beacons frame the jump and expose the depth of the gap.
        foreach (var x in new[] { -12.2, -8.6 })
        {
            BevelBox(scene, x, -1.95, -3.55, .28, 1.55, .36, .05, Dark);
            Box(scene, x, -1.8, -3.32, .10, 1.05, .055, Cyan);
        }
        for (var i = 0; i < 10; i++)
        {
            var x = -30 + i * 4.2; var height = 3 + i % 4;
            Box(scene, x, -12 + height / 2, -21 - i % 3 * 2, 1.7, height, 1.8, Dark);
            Box(scene, x - .73, -12 + height / 2, -20.04 - i % 3 * 2, .025, height * .85, .025, i % 2 == 0 ? Cyan : Pink);
        }
    }

    private static void CourseDeck(Model3DGroup scene, Point3D start, Point3D end, double width, double thickness)
    {
        var dx = end.X - start.X; var dz = end.Z - start.Z;
        var length = Math.Sqrt(dx * dx + dz * dz);
        var deck = new Model3DGroup();
        Box(deck, 0, -2.724 - thickness / 2, 0, width, thickness, length, Armor);
        Box(deck, -width / 2, -2.69, 0, .065, .065, length, Cyan);
        Box(deck, width / 2, -2.69, 0, .065, .065, length, Pink);
        for (var i = 1; i < length; i += 2)
        {
            Box(deck, 0, -2.707, -length / 2 + i, width - .15, .02, .035, Chrome);
            Box(deck, -.7, -2.69, -length / 2 + i, .13, .035, .65, Amber);
            Box(deck, .7, -2.69, -length / 2 + i, .13, .035, .65, Amber);
        }
        for (var i = 2; i < length; i += 6) Box(deck, 0, -7, -length / 2 + i, 1.0, 8, .75, Dark);
        var transform = new Transform3DGroup();
        transform.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0, 1, 0), Math.Atan2(dx, dz) * 180 / Math.PI)));
        transform.Children.Add(new TranslateTransform3D((start.X + end.X) / 2, 0, (start.Z + end.Z) / 2));
        deck.Transform = transform; scene.Children.Add(deck);
    }

    private static void BuildProjectionWall(Model3DGroup wall)
    {
        Box(wall, 2.1, 1.9, -5, 11, 10, .5, Surface("#121C2A", "#4D6C7C", .08));
        Box(wall, 2.1, 1.1, -4.7, 10.3, 4.75, .16, Dark);
        foreach (var x in new[] { -3.15, 7.35 })
        {
            Box(wall, x, 1.1, -4.55, .06, 4.95, .12, Cyan);
            for (var y = -1; y <= 3; y += 2) Sphere(wall, x, y, -4.43, .08, Chrome);
        }
        foreach (var y in new[] { -1.4, 3.6 }) Box(wall, 2.1, y, -4.55, 10.5, .035, .1, Pink);
        for (var i = 0; i < 5; i++)
        {
            var x = -2.8 + i * 2.4;
            Box(wall, x, 4.8, -4.65, 1.75, .045, .03, Cyan);
            Box(wall, x, -2.15, -4.65, 1.75, .045, .03, Pink);
        }
    }

    private static double HermiteVelocity(double a, double b, double va, double vb, double seconds, double u)
        => ((6 * u * u - 6 * u) * a + (3 * u * u - 4 * u + 1) * va * seconds
           + (-6 * u * u + 6 * u) * b + (3 * u * u - 2 * u) * vb * seconds) / seconds;
}

