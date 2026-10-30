using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace ReelGood;

internal sealed class GlitchyRig
{
    internal readonly AxisAngleRotation3D _spineBend = new(new Vector3D(1, 0, 0), 0);
    internal readonly AxisAngleRotation3D _spineTwist = new(new Vector3D(0, 1, 0), 0);
    internal readonly AxisAngleRotation3D _headTilt = new(new Vector3D(1, 0, 0), 0);
    internal readonly AxisAngleRotation3D _headTurn = new(new Vector3D(0, 1, 0), 0);
    internal readonly AxisAngleRotation3D[] _armSwings = new AxisAngleRotation3D[2], _wrists = new AxisAngleRotation3D[2], _ankles = new AxisAngleRotation3D[2], _coatFlaps = new AxisAngleRotation3D[2];
    internal readonly AxisAngleRotation3D[] _shoulders = new AxisAngleRotation3D[2], _elbows = new AxisAngleRotation3D[2], _hips = new AxisAngleRotation3D[2], _knees = new AxisAngleRotation3D[2], _reels = new AxisAngleRotation3D[2];
    internal Model3DGroup Model { get; }
    internal GlitchyRig(Model3DGroup? model = null) { Model = model ?? new Model3DGroup(); BuildCharacter(Model); }

    private void BuildCharacter(Model3DGroup root)
    {
        // Deliberately chunky, with a separate layered jacket silhouette.
        var jacket = Surface("#263351", "#879BCC", .13);
        var panels = Surface("#3C4469", "#A0ACD8", .12);
        var lapel = Surface("#53648A", "#BADEFF", .1);
        var cloth = Surface("#090F1B", "#344B69", .04);
        var glove = Surface("#192437", "#748BA8", .08);
        var violet = Surface("#262340", "#A76CD3", .1);
        var upper = new Transform3DGroup();
        upper.Children.Add(new RotateTransform3D(_spineBend, new Point3D(0, -.85, 0)));
        upper.Children.Add(new RotateTransform3D(_spineTwist, new Point3D(0, -.85, 0)));
        var body = new Model3DGroup { Transform = upper }; root.Children.Add(body);
        BevelBox(body, 0, -.24, 0, 1.52, 1.58, .90, .10, jacket);
        BevelBox(body, 0, -.31, .47, .57, 1.20, .06, .045, cloth);
        BevelBox(body, 0, -1.09, 0, 1.17, .30, .76, .045, cloth);
        BevelBox(body, 0, -1.06, .42, .30, .21, .08, .035, Chrome);
        Box(body, 0, -1.06, .47, .13, .08, .025, Cyan);
        foreach (var side in new[] { -1, 1 })
        {
            var accent = side < 0 ? Cyan : Pink;
            BevelBox(body, side * .49, -.39, .48, .48, 1.22, .15, .04, panels);
            // Broad extruded collar/lapel pieces read as clothing at intro scale.
            var collar = new Model3DGroup();
            Triangle(collar, new(side * .20, .70, -.13), new(side * .78, .39, .29), new(side * .25, -.26, .60), lapel);
            Triangle(collar, new(side * .20, .70, -.13), new(side * .25, -.26, .60), new(side * .13, .36, .38), panels);
            Triangle(collar, new(side * .20, .70, -.20), new(side * .78, .39, .22), new(side * .20, .70, -.13), jacket);
            body.Children.Add(collar);
            Tube(body, new(side * .20, .70, -.12), new(side * .78, .39, .30), .035, accent);
            Tube(body, new(side * .78, .39, .30), new(side * .25, -.26, .61), .029, accent);
            Tube(body, new(side * .25, -.27, .60), new(side * .25, -.94, .59), .022, accent);
            BevelBox(body, side * .50, -.62, .59, .32, .27, .08, .025, jacket);
            Box(body, side * .50, -.49, .64, .26, .035, .025, Chrome);
            var tail = Joint(body, side * .46, -.94, -.13, new Vector3D(1, 0, 0), out _coatFlaps[side < 0 ? 0 : 1]);
            BevelBox(tail, 0, -.24, -.02, .52, .55, .78, .035, jacket);
            Box(tail, side * .22, -.46, .39, .035, .10, .03, accent);
        }
        // Bright back yoke and hem make the jacket distinct from the trousers.
        BevelBox(body, 0, .25, -.49, 1.25, .26, .12, .04, panels);
        Box(body, 0, .28, -.56, .98, .055, .025, Cyan);
        BevelBox(body, 0, -.40, -.49, .87, .64, .10, .045, cloth);
        Tube(body, new(-.28, -.60, -.56), new(.28, -.18, -.56), .035, Pink);
        Tube(body, new(-.28, -.18, -.56), new(.28, -.60, -.56), .035, Cyan);
        Box(body, 0, -.97, -.50, 1.22, .08, .06, panels);
        Loft(body, 0, .58, 0, new[] { (.12, .18, .17, 0d), (-.16, .21, .19, 0d) }, 8, Dark);
        var headTransform = new Transform3DGroup();
        headTransform.Children.Add(new RotateTransform3D(_headTilt, new Point3D(0, .65, 0)));
        headTransform.Children.Add(new RotateTransform3D(_headTurn, new Point3D(0, .65, 0)));
        var head = new Model3DGroup { Transform = headTransform }; body.Children.Add(head);
        // Camera housing remains recognizable as a camera, with an octagonal
        // chamfered body and a square matte-box frame around the circular glass.
        Loft(head, 0, 0, .035, new[] {
            (1.80, .61, .37, 0d), (1.64, .84, .49, 0d),
            (.85, .79, .46, 0d), (.65, .60, .32, -.03)
        }, 8, Armor);
        BevelBox(head, 0, 1.22, .53, 1.55, .96, .14, .19, Dark);
        LensHood(head, .68, .55, .51, .43, 1.13, .82, Chrome);
        LensHood(head, .65, .52, .57, .46, 1.16, 1.145, panels);
        Cylinder(head, 0, 1.18, .78, .48, .25, Dark);
        Cylinder(head, 0, 1.18, .94, .45, .08, Chrome);
        Cylinder(head, 0, 1.18, 1.00, .41, .045, Pink);
        Cylinder(head, 0, 1.18, 1.04, .37, .035, Dark);
        Cylinder(head, 0, 1.18, 1.075, .33, .028, Cyan);
        Cylinder(head, 0, 1.18, 1.10, .28, .025, violet);
        Cylinder(head, 0, 1.18, 1.123, .21, .019, Cyan);
        Cylinder(head, 0, 1.18, 1.14, .13, .015, Surface("#276D94", "#C3FFFF", .85));
        FacetEllipsoid(head, -.10, 1.29, 1.16, .055, .055, .02, Surface("#DBFFFF", "#FFFFFF", 1));
        foreach (var side in new[] { -1, 1 })
        {
            Tube(head, new(side * .73, .90, .60), new(side * .73, 1.60, .60), .019, side == -1 ? Cyan : Pink);
            for (var k = 0; k < 4; k++) Tube(head, new(side * .81, 1.00 + k * .12, -.25), new(side * .81, 1.00 + k * .12, .14), .022, Chrome);
            Cylinder(head, side * .61, 1.61, .59, .06, .05, Chrome);
        }
        Cylinder(head, -.65, 1.43, .66, .14, .22, Dark);
        Cylinder(head, -.65, 1.43, .79, .12, .035, Chrome);
        Cylinder(head, -.65, 1.43, .815, .085, .02, Cyan);

        for (var side = 0; side < 2; side++)
        {
            var sign = side == 0 ? -1 : 1;
            var accent = side == 0 ? Cyan : Pink;
            var reel = Joint(head, sign * .51, 2.12, .02, new Vector3D(0, 0, 1), out _reels[side]);
            Cylinder(reel, 0, 0, -.02, .55, .24, Dark);
            Cylinder(reel, 0, 0, .115, .55, .04, accent);
            Cylinder(reel, 0, 0, .145, .515, .025, Chrome);
            Cylinder(reel, 0, 0, .17, .465, .02, Dark);
            // Five broad swept spokes match the icon's open reel design.
            for (var k = 0; k < 5; k++)
            {
                var a = k * Math.PI * 2 / 5;
                Point3D P(double r, double theta) => new(r * Math.Cos(theta), r * Math.Sin(theta), .19);
                Triangle(reel, P(.12, a - .35), P(.46, a - .13), P(.46, a + .23), panels);
                Triangle(reel, P(.12, a - .35), P(.46, a + .23), P(.15, a + .40), Chrome);
            }
            Cylinder(reel, 0, 0, .205, .14, .045, Chrome);
            Cylinder(reel, 0, 0, .235, .10, .02, accent);
            Cylinder(reel, 0, 0, .25, .06, .015, Dark);

        }
        for (var side = 0; side < 2; side++)
        {
            var sign = side == 0 ? -1 : 1;
            var accent = side == 0 ? Cyan : Pink;
            var arm = Joint(body, sign * .93, .23, 0, new Vector3D(0, 0, 1), out _shoulders[side]);
            AddRotation(arm, new Vector3D(1, 0, 0), out _armSwings[side]);
            BevelBox(arm, 0, -.33, 0, .53, .82, .64, .06, jacket);
            BevelBox(arm, sign * .28, -.22, .015, .09, .28, .38, .02, panels);
            Box(arm, sign * .335, -.22, .02, .035, .18, .18, accent);
            var forearm = Joint(arm, 0, -.82, 0, new Vector3D(1, 0, 0), out _elbows[side]);
            BevelBox(forearm, 0, -.28, 0, .47, .64, .56, .05, jacket);
            BevelBox(forearm, 0, -.52, .015, .50, .15, .59, .025, panels);
            Box(forearm, 0, -.31, .30, .25, .14, .035, accent);
            var hand = Joint(forearm, 0, -.66, .02, new Vector3D(1, 0, 0), out _wrists[side]);
            BevelBox(hand, 0, -.12, .02, .39, .35, .35, .055, glove);
            for (var k = 0; k < 3; k++) BevelBox(hand, -.12 + k * .12, -.12, .21, .085, .22, .09, .025, Chrome);
            BevelBox(hand, -sign * .24, .02, .02, .16, .35, .20, .04, glove);
            var leg = Joint(root, sign * .35, -1.30, 0, new Vector3D(1, 0, 0), out _hips[side]);
            BevelBox(leg, 0, -.27, 0, .49, .65, .55, .035, cloth);
            BevelBox(leg, sign * .24, -.26, .03, .09, .30, .39, .02, jacket);
            var shin = Joint(leg, 0, -.65, 0, new Vector3D(1, 0, 0), out _knees[side]);
            BevelBox(shin, 0, -.27, 0, .44, .62, .50, .035, cloth);
            Box(shin, sign * .19, -.29, .26, .04, .35, .035, accent);
            var foot = Joint(shin, 0, -.52, .02, new Vector3D(1, 0, 0), out _ankles[side]);
            BevelBox(foot, 0, -.07, .16, .56, .30, .91, .045, glove);
            Box(foot, 0, -.23, .17, .57, .045, .90, panels);
            Box(foot, 0, -.15, .62, .40, .05, .035, accent);
        }
    }

    private static void AddRotation(Model3DGroup joint, Vector3D axis, out AxisAngleRotation3D rotation)
    {
        rotation = new AxisAngleRotation3D(axis, 0);
        ((Transform3DGroup)joint.Transform).Children.Insert(1, new RotateTransform3D(rotation));
    }
    private static void Loft(Model3DGroup group, double x, double y, double z,
        (double Y, double XRadius, double ZRadius, double ZOffset)[] rings, int sides, Material material)
    {
        var mesh = new MeshGeometry3D();
        Point3D P(int ring, int side)
        {
            var angle = (side + .5) * Math.PI * 2 / sides;
            var r = rings[ring]; return new(r.XRadius * Math.Cos(angle), r.Y, r.ZOffset + r.ZRadius * Math.Sin(angle));
        }
        for (var ring = 0; ring < rings.Length - 1; ring++)
            for (var side = 0; side < sides; side++)
                Face(mesh, P(ring, side), P(ring + 1, side), P(ring + 1, side + 1), P(ring, side + 1));
        for (var side = 0; side < sides; side++)
        {
            Tri(mesh, new(0, rings[0].Y, rings[0].ZOffset), P(0, side + 1), P(0, side));
            var last = rings.Length - 1;
            Tri(mesh, new(0, rings[last].Y, rings[last].ZOffset), P(last, side), P(last, side + 1));
        }
        mesh.Freeze(); group.Children.Add(new GeometryModel3D(mesh, material) { BackMaterial = material, Transform = new TranslateTransform3D(x, y, z) });
    }

    private static void FacetEllipsoid(Model3DGroup group, double x, double y, double z, double rx, double ry, double rz, Material material)
    {
        var rings = Enumerable.Range(0, 7).Select(i =>
        {
            var angle = i * Math.PI / 6;
            return (Y: Math.Cos(angle) * ry, XRadius: Math.Max(.001, Math.Sin(angle) * rx), ZRadius: Math.Max(.001, Math.Sin(angle) * rz), ZOffset: 0d);
        }).ToArray();
        Loft(group, x, y, z, rings, 8, material);
    }

    internal static void Tube(Model3DGroup group, Point3D a, Point3D b, double radius, Material material)
    {
        var direction = b - a; if (direction.LengthSquared < 1e-10) return;
        direction.Normalize(); var tangent = Vector3D.CrossProduct(direction, new Vector3D(0, 0, 1));
        if (tangent.LengthSquared < .01) tangent = Vector3D.CrossProduct(direction, new Vector3D(0, 1, 0));
        tangent.Normalize(); var bitangent = Vector3D.CrossProduct(direction, tangent);
        var mesh = new MeshGeometry3D();
        for (var i = 0; i < 6; i++)
        {
            Vector3D Offset(int k) { var angle = k * Math.PI / 3; return (tangent * Math.Cos(angle) + bitangent * Math.Sin(angle)) * radius; }
            Face(mesh, a + Offset(i), b + Offset(i), b + Offset(i + 1), a + Offset(i + 1));
        }
        mesh.Freeze(); group.Children.Add(new GeometryModel3D(mesh, material) { BackMaterial = material });
    }

    private static void LensHood(Model3DGroup group, double outerX, double outerY, double innerX, double innerY, double frontZ, double backZ, Material material)
    {
        var contour = new[] { new Point(-.76, -1), new Point(.76, -1), new Point(1, -.76), new Point(1, .76), new Point(.76, 1), new Point(-.76, 1), new Point(-1, .76), new Point(-1, -.76) };
        var mesh = new MeshGeometry3D();
        for (var i = 0; i < contour.Length; i++)
        {
            Point3D P(int k, double width, double height, double depth) => new(contour[k % 8].X * width, 1.18 + contour[k % 8].Y * height, depth);
            Face(mesh, P(i, outerX, outerY, frontZ), P(i + 1, outerX, outerY, frontZ), P(i + 1, innerX, innerY, backZ), P(i, innerX, innerY, backZ));
        }
        mesh.Freeze(); group.Children.Add(new GeometryModel3D(mesh, material) { BackMaterial = material });
    }
    private static readonly Material Armor = Surface("#142535", "#739CAF", .1);
    private static readonly Material Dark = Surface("#070D17", "#33536B", .05);
    private static readonly Material Chrome = Surface("#415A70", "#D8F5FF", .05);
    private static readonly Material Cyan = Surface("#167D9B", "#50F2FF", .8);
    private static readonly Material Pink = Surface("#762455", "#FF4DD0", .8);
    private static readonly Material Amber = Surface("#BB7530", "#FFD88C", .6);

    private static Model3DGroup Joint(Model3DGroup parent, double x, double y, double z, Vector3D axis, out AxisAngleRotation3D rotation)
    {
        rotation = new AxisAngleRotation3D(axis, 0);
        var transform = new Transform3DGroup(); transform.Children.Add(new RotateTransform3D(rotation)); transform.Children.Add(new TranslateTransform3D(x, y, z));
        var group = new Model3DGroup { Transform = transform }; parent.Children.Add(group); return group;
    }
    internal static Material Surface(string baseColor, string glow, double emission)
    {
        var a = (Color)ColorConverter.ConvertFromString(baseColor); var b = (Color)ColorConverter.ConvertFromString(glow);
        var group = new MaterialGroup();
        group.Children.Add(new DiffuseMaterial(new SolidColorBrush(a)));
        group.Children.Add(new SpecularMaterial(new SolidColorBrush(b), 70));
        group.Children.Add(new EmissiveMaterial(new SolidColorBrush(Color.FromArgb((byte)(emission * 210), b.R, b.G, b.B))));
        group.Freeze(); return group;
    }
    internal static void Box(Model3DGroup g, double x, double y, double z, double w, double h, double d, Material m) => BevelBox(g, x, y, z, w, h, d, 0, m);
    internal static void BevelBox(Model3DGroup g, double x, double y, double z, double w, double h, double d, double b, Material m)
    {
        // Eight corner cuts produce real chamfered silhouettes and specular edge faces.
        var mesh = new MeshGeometry3D();
        var corners = new[] { new Point(-w / 2 + b, -h / 2), new Point(w / 2 - b, -h / 2), new Point(w / 2, -h / 2 + b), new Point(w / 2, h / 2 - b), new Point(w / 2 - b, h / 2), new Point(-w / 2 + b, h / 2), new Point(-w / 2, h / 2 - b), new Point(-w / 2, -h / 2 + b) };
        for (var i = 0; i < 8; i++)
        {
            var a = corners[i]; var c = corners[(i + 1) % 8];
            Face(mesh, new(a.X, a.Y, d / 2), new(c.X, c.Y, d / 2), new(c.X, c.Y, -d / 2), new(a.X, a.Y, -d / 2));
            Tri(mesh, new(0, 0, d / 2), new(a.X, a.Y, d / 2), new(c.X, c.Y, d / 2));
            Tri(mesh, new(0, 0, -d / 2), new(c.X, c.Y, -d / 2), new(a.X, a.Y, -d / 2));
        }
        mesh.Freeze(); g.Children.Add(new GeometryModel3D(mesh, m) { BackMaterial = m, Transform = new TranslateTransform3D(x, y, z) });
    }
    internal static void Cylinder(Model3DGroup g, double x, double y, double z, double radius, double depth, Material material)
    {
        var mesh = new MeshGeometry3D(); const int slices = 36;
        for (var i = 0; i < slices; i++)
        {
            var a = i * 2 * Math.PI / slices; var b = (i + 1) * 2 * Math.PI / slices;
            var p = new Point3D(radius * Math.Cos(a), radius * Math.Sin(a), depth / 2);
            var q = new Point3D(radius * Math.Cos(b), radius * Math.Sin(b), depth / 2);
            Face(mesh, p, new(p.X, p.Y, -depth / 2), new(q.X, q.Y, -depth / 2), q);
            Tri(mesh, new(0, 0, depth / 2), p, q);
            Tri(mesh, new(0, 0, -depth / 2), new(q.X, q.Y, -depth / 2), new(p.X, p.Y, -depth / 2));
        }
        mesh.Freeze(); g.Children.Add(new GeometryModel3D(mesh, material) { BackMaterial = material, Transform = new TranslateTransform3D(x, y, z) });
    }
    private static void Sphere(Model3DGroup g, double x, double y, double z, double r, Material m)
    {
        var mesh = new MeshGeometry3D();
        for (var j = 0; j < 8; j++) for (var i = 0; i < 16; i++)
        {
            Point3D P(int u, int v) { var a = u * Math.PI * 2 / 16; var b = v * Math.PI / 8; return new(r * Math.Sin(b) * Math.Cos(a), r * Math.Cos(b), r * Math.Sin(b) * Math.Sin(a)); }
            Face(mesh, P(i, j), P(i, j + 1), P(i + 1, j + 1), P(i + 1, j));
        }
        mesh.Freeze(); g.Children.Add(new GeometryModel3D(mesh, m) { BackMaterial = m, Transform = new TranslateTransform3D(x, y, z) });
    }
    private static void Tri(MeshGeometry3D mesh, Point3D a, Point3D b, Point3D c)
    {
        var n = Vector3D.CrossProduct(b - a, c - a); if (n.LengthSquared > 1e-12) n.Normalize();
        var start = mesh.Positions.Count;
        foreach (var p in new[] { a, b, c }) { mesh.Positions.Add(p); mesh.Normals.Add(n); }
        mesh.TriangleIndices.Add(start); mesh.TriangleIndices.Add(start + 1); mesh.TriangleIndices.Add(start + 2);
    }
    private static void Face(MeshGeometry3D mesh, Point3D a, Point3D b, Point3D c, Point3D d) { Tri(mesh, a, b, c); Tri(mesh, a, c, d); }
    private static void Triangle(Model3DGroup g, Point3D a, Point3D b, Point3D c, Material m)
    {
        var mesh = new MeshGeometry3D(); Tri(mesh, a, b, c); mesh.Freeze(); g.Children.Add(new GeometryModel3D(mesh, m) { BackMaterial = m });
    }
    internal static void Quad(Model3DGroup g, Point3D a, Point3D b, Point3D c, Point3D d, Material m)
    {
        var mesh = new MeshGeometry3D { Positions = new Point3DCollection { a, b, c, d }, TextureCoordinates = new PointCollection { new(0, 1), new(1, 1), new(1, 0), new(0, 0) }, TriangleIndices = new Int32Collection { 0, 1, 2, 0, 2, 3 } };
        mesh.Freeze(); g.Children.Add(new GeometryModel3D(mesh, m) { BackMaterial = m });
    }

}


