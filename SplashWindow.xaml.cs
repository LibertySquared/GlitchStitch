using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;

namespace ReelGood;

// An articulated mesh character, not an image plane. A single WPF animation clock
// drives choreography, projection and camera so seeks/capture remain deterministic.
public partial class SplashWindow : Window
{
    public static readonly DependencyProperty SceneTimeProperty = DependencyProperty.Register(
        nameof(SceneTime), typeof(double), typeof(SplashWindow), new PropertyMetadata(0d, (d, e) => ((SplashWindow)d).Pose((double)e.NewValue)));
    public double SceneTime { get => (double)GetValue(SceneTimeProperty); set => SetValue(SceneTimeProperty, value); }
    private readonly CancellationTokenSource _introSoundCancellation = new();
    private bool _introStarted;
    private readonly TranslateTransform3D _position = new();
    private readonly AxisAngleRotation3D _yaw = new(new Vector3D(0, 1, 0), 0);
    private readonly AxisAngleRotation3D _flip = new(new Vector3D(1, 0, 0), 0);
    private readonly AxisAngleRotation3D _roll = new(new Vector3D(0, 0, 1), 0);
    private AxisAngleRotation3D[] _shoulders => _mascotRig._shoulders;
    private AxisAngleRotation3D[] _elbows => _mascotRig._elbows;
    private AxisAngleRotation3D[] _hips => _mascotRig._hips;
    private AxisAngleRotation3D[] _knees => _mascotRig._knees;
    private AxisAngleRotation3D[] _reels => _mascotRig._reels;
    private readonly List<(TranslateTransform3D Position, ScaleTransform3D Scale, double Seed)> _sparks = new();
    private readonly SolidColorBrush _beamBrush = new(Color.FromArgb(0, 65, 225, 255));
    private readonly SolidColorBrush _rimBeamBrush = new(Color.FromArgb(0, 245, 65, 205));
    private readonly ImageBrush _projection = new() { Opacity = 0 };
    private readonly TranslateTransform3D _shadowPosition = new();
    private readonly ScaleTransform3D _shadowScale = new();
    private readonly ScaleTransform3D _impactScale = new(0, 0, 0);
    private readonly PerspectiveCamera _camera = new() { FieldOfView = 49, UpDirection = new Vector3D(0, 1, 0) };
    private static readonly Material Armor = Surface("#142535", "#739CAF", .1);
    private static readonly Material Dark = Surface("#070D17", "#33536B", .05);
    private static readonly Material Chrome = Surface("#415A70", "#D8F5FF", .05);
    private static readonly Material Cyan = Surface("#167D9B", "#50F2FF", .8);
    private static readonly Material Pink = Surface("#762455", "#FF4DD0", .8);
    private static readonly Material Amber = Surface("#BB7530", "#FFD88C", .6);

    public SplashWindow()
    {
        InitializeComponent();
        BuildStudioScene();
        Pose(0);
        Closed += (_, _) =>
        {
            _introSoundCancellation.Cancel();
            ((Storyboard)FindResource("Intro")).Remove(this);
        };
    }

    public Task PlayIntroAsync()
    {
        if (_introStarted) return _introCompletion.Task;
        _introStarted = true;
        _ = IntroSound.PlayAsync(_introSoundCancellation.Token);
        var intro = (Storyboard)FindResource("Intro");
        intro.Completed += (_, _) => _introCompletion.TrySetResult();
        intro.Begin(this, true);
        return _introCompletion.Task;
    }
    private readonly TaskCompletionSource _introCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private void BuildStudioScene()
    {
        Stage.Camera = _camera;
        var scene = new Model3DGroup();
        scene.Children.Add(new AmbientLight(Color.FromRgb(58, 78, 109)));
        scene.Children.Add(new DirectionalLight(Color.FromRgb(185, 228, 255), new Vector3D(-.5, -.8, -1)));
        scene.Children.Add(new PointLight(Color.FromRgb(255, 38, 180), new Point3D(-6, 4, 3)) { Range = 22 });
        scene.Children.Add(new PointLight(Color.FromRgb(25, 230, 255), new Point3D(5, 4, 3)) { Range = 22 });
        scene.Children.Add(new PointLight(Color.FromRgb(50, 155, 215), new Point3D(-17, 5, -13)) { Range = 32 });
        BuildPlatformCourse(scene);
        var wall = new Model3DGroup();
        BuildProjectionWall(wall);
        scene.Children.Add(wall);
        var shadow = new Model3DGroup();
        var shadowBrush = new RadialGradientBrush(Color.FromArgb(185, 0, 0, 0), Colors.Transparent);
        Quad(shadow, new(-1.9, 0, 1.2), new(1.9, 0, 1.2), new(1.9, 0, -1.2), new(-1.9, 0, -1.2), new DiffuseMaterial(shadowBrush));
        var shadowTransforms = new Transform3DGroup(); shadowTransforms.Children.Add(_shadowScale); shadowTransforms.Children.Add(_shadowPosition);
        shadow.Transform = shadowTransforms; scene.Children.Add(shadow);
        _projection.ImageSource = TitleTexture();
        var title = new EmissiveMaterial(_projection);
        Quad(wall, new(-2.95, -1.2, -4.48), new(7.15, -1.2, -4.48), new(7.15, 3.4, -4.48), new(-2.95, 3.4, -4.48), title);
        var character = new Model3DGroup();
        var transform = new Transform3DGroup();
        transform.Children.Add(new RotateTransform3D(_flip));
        transform.Children.Add(new RotateTransform3D(_roll));
        transform.Children.Add(new RotateTransform3D(_yaw));
        transform.Children.Add(_position);
        character.Transform = transform;
        _characterModel = character;
        BuildCharacter(character);
        scene.Children.Add(character);
        // The final head orientation is 132 degrees: the lens points toward this wall.
        var lens = new Point3D(-3.13, 1.18, .22);
        var beamMaterial = new EmissiveMaterial(_beamBrush);
        var beamEdge = new EmissiveMaterial(_rimBeamBrush);
        var corners = new[] { new Point3D(-2.95, -1.2, -4.44), new Point3D(7.15, -1.2, -4.44), new Point3D(7.15, 3.4, -4.44), new Point3D(-2.95, 3.4, -4.44) };
        for (var i = 0; i < 4; i++) Triangle(wall, lens, corners[i], corners[(i + 1) % 4], i % 2 == 0 ? beamMaterial : beamEdge);
        var impact = new Model3DGroup { Transform = _impactScale };
        for (var i = 0; i < 48; i++)
        {
            var a = i * Math.PI / 24;
            Box(impact, Math.Cos(a) * 1.3, -.01, Math.Sin(a) * 1.3, .14, .025, .14, i % 3 == 0 ? Pink : Cyan);
        }
        var impactRoot = new Model3DGroup { Transform = new TranslateTransform3D(-4, -2.69, 1) };
        impactRoot.Children.Add(impact); scene.Children.Add(impactRoot);
        for (var i = 0; i < 60; i++)
        {
            var spark = new Model3DGroup();
            Box(spark, 0, 0, 0, .035, .035, .28, i % 3 == 0 ? Pink : Cyan);
            var scale = new ScaleTransform3D(); var position = new TranslateTransform3D();
            var transforms = new Transform3DGroup(); transforms.Children.Add(scale); transforms.Children.Add(position);
            spark.Transform = transforms; scene.Children.Add(spark); _sparks.Add((position, scale, i / 60d));
        }
        Stage.Children.Add(new ModelVisual3D { Content = scene });
    }

    private void Pose(double t)
    {
        if (_shoulders[0] is null) return;
        var motion = SampleIntro(t);
        _spineBend.Angle = _spineTwist.Angle = _headTilt.Angle = _headTurn.Angle = 0;
        if (t < .8 || t is >= 1.7 and < 2.0)
        {
            var firstRun = t < .8;
            var u = firstRun ? t / .8 : (t - 1.7) / .3;
            var gather = Smooth(Math.Clamp((u - .75) / .25, 0, 1));
            var phase = firstRun ? t * Math.PI * 7 : (t - 1.7) * Math.PI * 6;
            _spineBend.Angle = 11 * (1 - gather);
            if (!firstRun) _spineBend.Angle *= Smooth(Math.Clamp(u / .35, 0, 1));
            _headTilt.Angle = -_spineBend.Angle * .6;
            _spineTwist.Angle = 6 * Math.Sin(phase) * (1 - gather);
            for (var i = 0; i < 2; i++)
            {
                var stride = Math.Sin(phase + i * Math.PI);
                _hips[i].Angle = Lerp(stride * 40, -10, gather);
                _knees[i].Angle = Lerp(Math.Max(0, -stride) * 56, 20, gather);
                _ankles[i].Angle = -(_hips[i].Angle + _knees[i].Angle);
                _shoulders[i].Angle = (i == 0 ? -1 : 1) * (6 + 9 * gather);
                _armSwings[i].Angle = Lerp(-stride * 43, 28, gather);
                _elbows[i].Angle = Lerp(-30 - Math.Max(0, stride) * 15, -55, gather);
                _wrists[i].Angle = 8 * stride * (1 - gather);
                if (!firstRun)
                {
                    var landingBlend = 1 - Smooth(Math.Clamp(u / .35, 0, 1));
                    _hips[i].Angle = Lerp(_hips[i].Angle, -10, landingBlend);
                    _knees[i].Angle = Lerp(_knees[i].Angle, 20, landingBlend);
                    _ankles[i].Angle = Lerp(_ankles[i].Angle, -10, landingBlend);
                    _shoulders[i].Angle = Lerp(_shoulders[i].Angle, (i == 0 ? -1 : 1) * 15, landingBlend);
                    _armSwings[i].Angle = Lerp(_armSwings[i].Angle, -12, landingBlend);
                    _elbows[i].Angle = Lerp(_elbows[i].Angle, -24, landingBlend);
                    _wrists[i].Angle *= 1 - landingBlend;
                }
            }
        }
        else if (t < 1.7)
        {
            var p = (t - .8) / .9; var tuck = Math.Sin(Math.PI * p);
            _spineBend.Angle = 27 * tuck; _headTilt.Angle = -16 * tuck;
            for (var i = 0; i < 2; i++)
            {
                _hips[i].Angle = -10 - 88 * tuck; _knees[i].Angle = 20 + 115 * tuck;
                _ankles[i].Angle = -10 - 18 * tuck;
                _shoulders[i].Angle = (i == 0 ? -1 : 1) * (15 + 22 * tuck);
                _armSwings[i].Angle = Lerp(28, -12, Smooth(p)) - 70 * tuck;
                _elbows[i].Angle = Lerp(-55, -24, Smooth(p)) - 58 * tuck;
                _wrists[i].Angle = -15 * tuck;
            }
        }
        else if (t < 3.25)
        {
            var p = (t - 2) / 1.25; var extend = Math.Sin(Math.PI * p);
            _spineBend.Angle = 12 * extend + 2.85 * Smooth(p);
            _spineTwist.Angle = 15 * Math.Sin(p * Math.PI * 2) * extend;
            _headTilt.Angle = -10 * extend - 1.8 * Smooth(p); _headTurn.Angle = -_spineTwist.Angle * .5;
            for (var i = 0; i < 2; i++)
            {
                var side = i == 0 ? -1 : 1;
                _hips[i].Angle = -10 - 34 * extend + side * 18 * extend;
                _knees[i].Angle = 20 + 55 * extend - side * 20 * extend;
                _ankles[i].Angle = -10 - 14 * extend;
                _shoulders[i].Angle = side * (15 + (i == 0 ? 64 : 36) * extend);
                _armSwings[i].Angle = Lerp(28, -12, Smooth(p)) - side * 35 * extend;
                _elbows[i].Angle = Lerp(-55, -24, Smooth(p)) - 25 * extend;
                _wrists[i].Angle = side * 20 * extend;
            }
        }
        else
        {
            var u = Math.Clamp((t - 3.25) / .55, 0, 1);
            var crouch = .15 * (1 - Smooth(u)) + .60 * Math.Sin(u * Math.PI);
            _spineBend.Angle = 19 * crouch; _headTilt.Angle = -12 * crouch;
            for (var i = 0; i < 2; i++)
            {
                var side = i == 0 ? -1 : 1;
                _hips[i].Angle = -65 * crouch; _knees[i].Angle = 130 * crouch; _ankles[i].Angle = -65 * crouch;
                _shoulders[i].Angle = side * (15 + 16 * Math.Sin(u * Math.PI));
                _armSwings[i].Angle = -12 - side * 10 * Math.Sin(u * Math.PI);
                _elbows[i].Angle = -24 - 25 * Math.Sin(u * Math.PI);
                _wrists[i].Angle = side * 8 * Math.Sin(u * Math.PI);
            }
            var settle = Smooth(Math.Clamp((t - 3.7) / .35, 0, 1));
            _shoulders[1].Angle = Lerp(_shoulders[1].Angle, 42, settle);
            _armSwings[1].Angle = Lerp(_armSwings[1].Angle, -20, settle);
            _elbows[1].Angle = Lerp(_elbows[1].Angle, -112, settle);
            _wrists[1].Angle = Lerp(_wrists[1].Angle, 12, settle);
        }
        _position.OffsetX = motion.X; _position.OffsetY = motion.Y; _position.OffsetZ = motion.Z;
        _flip.Angle = motion.Pitch; _roll.Angle = motion.Roll; _yaw.Angle = motion.Yaw;
        for (var i = 0; i < 2; i++)
        {
            _reels[i].Angle = t * (i == 0 ? 330 : -300);
            var moving = t < 4.0 ? Math.Sin(Math.PI * Math.Clamp(t / 4, 0, 1)) : 0;
            _coatFlaps[i].Angle = moving * (8 + 8 * Math.Sin(t * 11 - i * .6));
        }
        if (_characterModel is not null)
        {
            var floorOffset = -2.724 - _characterModel.Bounds.Y;
            var grounded = t < .8 || t is >= 1.7 and < 2 || t >= 3.25;
            if (grounded || floorOffset > 0) _position.OffsetY += floorOffset;
        }
        _shadowPosition.OffsetX = motion.X; _shadowPosition.OffsetY = -2.71; _shadowPosition.OffsetZ = motion.Z;
        var overGap = t is > 2.08 and < 3.10;
        _shadowScale.ScaleX = _shadowScale.ScaleZ = overGap ? 0 : 1 + Math.Max(0, _position.OffsetY) * .22;
        var projection = Math.Clamp((t - 4.1) / .35, 0, 1);
        if (t is > 4.1 and < 4.38) projection *= .45 + .55 * Math.Abs(Math.Sin(t * 77));
        _projection.Opacity = projection;
        _beamBrush.Color = Color.FromArgb((byte)(projection * 22), 60, 225, 255);
        _rimBeamBrush.Color = Color.FromArgb((byte)(projection * 12), 240, 60, 210);
        var ring = Math.Clamp((t - 3.25) / .38, 0, 1);
        _impactScale.ScaleX = _impactScale.ScaleZ = t is >= 3.25 and < 3.63 ? .2 + ring * 2.8 : 0;
        _impactScale.ScaleY = 1;
        foreach (var spark in _sparks)
        {
            var birth = 2 + spark.Seed * 1.25; var age = t - birth;
            var point = SampleIntro(birth);
            spark.Position.OffsetX = point.X - age * .8;
            spark.Position.OffsetY = point.Y - age * 1.6;
            spark.Position.OffsetZ = point.Z + age * .6;
            spark.Scale.ScaleX = spark.Scale.ScaleY = spark.Scale.ScaleZ = age is > 0 and < .30 ? 1 - age / .30 : 0;
        }
        // A real perspective approach: no character scaling, no wall fade/pop.
        // The camera initially looks down the distant course, then pans right.
        var reveal = Smooth(Math.Clamp((t - 2.7) / 1.3, 0, 1));
        var actionWide = Smooth(Math.Clamp((t - 1.7) / .3, 0, 1));
        _camera.FieldOfView = Lerp(Lerp(42, 56, actionWide), 49, reveal);
        _camera.Position = new Point3D(Lerp(-2, 6, reveal), Lerp(5, 3.2, reveal), Lerp(18, 17.5, reveal));
        var airborne = t is >= .8 and < 1.7 || t is >= 2 and < 3.25;
        var actorBounds = _characterModel?.Bounds ?? Rect3D.Empty;
        var targetY = airborne && !actorBounds.IsEmpty ? actorBounds.Y + actorBounds.SizeY / 2 : .5;
        var target = new Point3D(Lerp(motion.X - 3, .3, reveal), Lerp(targetY, .5, reveal), Lerp(motion.Z - 3, -1.5, reveal));
        _camera.LookDirection = target - _camera.Position;
        // Frame the whole articulated bounding box, including boots/reels at the
        // widest part of the corkscrew. This also handles wide monitor ratios.
        if (airborne && !actorBounds.IsEmpty)
        {
            var look = _camera.LookDirection; look.Normalize();
            var right = Vector3D.CrossProduct(look, _camera.UpDirection); right.Normalize();
            var up = Vector3D.CrossProduct(right, look); up.Normalize();
            var aspect = Stage.ActualHeight > 0 ? Stage.ActualWidth / Stage.ActualHeight : 1500d / 960;
            var required = 0d;
            foreach (var bx in new[] { actorBounds.X, actorBounds.X + actorBounds.SizeX })
            foreach (var by in new[] { actorBounds.Y, actorBounds.Y + actorBounds.SizeY })
            foreach (var bz in new[] { actorBounds.Z, actorBounds.Z + actorBounds.SizeZ })
            {
                var relative = new Point3D(bx, by, bz) - _camera.Position;
                var depth = Vector3D.DotProduct(relative, look);
                if (depth <= 0) continue;
                required = Math.Max(required, Math.Abs(Vector3D.DotProduct(relative, up) / depth) * aspect);
                required = Math.Max(required, Math.Abs(Vector3D.DotProduct(relative, right) / depth));
            }
            _camera.FieldOfView = Math.Max(_camera.FieldOfView, Math.Atan(required * 1.08) * 360 / Math.PI);
        }
        SequenceLabel.Text = t < .8 ? "GLITCHY // INBOUND SIGNAL" : t < 1.7 ? "VAULT // SOMERSAULT" : t < 2 ? "GAP AHEAD // LAUNCH" : t < 3.25 ? "CORKSCREW // CROSSING THE VOID" : t < 4.1 ? "CHECKPOINT // PROJECTOR ONLINE" : "TRANSMISSION LOCKED // WELCOME TO GLITCHSTICH";
    }
    private static double CrouchDrop(double amount) => 1.25 * (1 - Math.Cos(65 * amount * Math.PI / 180));
    private static double Hermite(double a, double b, double va, double vb, double seconds, double u)
        => (2 * u * u * u - 3 * u * u + 1) * a + (u * u * u - 2 * u * u + u) * va * seconds
         + (-2 * u * u * u + 3 * u * u) * b + (u * u * u - u * u) * vb * seconds;
    private static double Smooth(double u) => u * u * (3 - 2 * u);
    private static double Lerp(double a, double b, double u) => a + (b - a) * u;
    private static BitmapSource TitleTexture()
    {
        var panel = new Grid { Width = 1536, Height = 680, Background = Brushes.Transparent };
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(55) };
        stack.Children.Add(new TextBlock { Text = "// GLITCHY TRANSMISSION", FontFamily = new FontFamily("Consolas"), FontSize = 28, Foreground = new SolidColorBrush(Color.FromRgb(65, 225, 255)), Margin = new Thickness(4, 0, 0, 22) });
        stack.Children.Add(new TextBlock { Text = "GLITCHSTICH", FontFamily = new FontFamily("Segoe UI"), FontWeight = FontWeights.Black, FontSize = 169, Foreground = Brushes.White });
        stack.Children.Add(new Border { Height = 7, Background = new SolidColorBrush(Color.FromRgb(245, 60, 205)), Margin = new Thickness(6, 26, 26, 35) });
        stack.Children.Add(new TextBlock { Text = "GLITCHES IN. STITCHES OUT.", FontFamily = new FontFamily("Consolas"), FontSize = 43, Foreground = new SolidColorBrush(Color.FromRgb(65, 225, 255)) });
        stack.Children.Add(new TextBlock { Text = "CAPTURE THE CHAOS. STITCH YOUR STORY.", FontFamily = new FontFamily("Consolas"), FontSize = 24, Foreground = Brushes.LightSteelBlue, Margin = new Thickness(0, 28, 0, 0) });
        panel.Children.Add(stack); panel.Measure(new Size(1536, 680)); panel.Arrange(new Rect(0, 0, 1536, 680)); panel.UpdateLayout();
        var bitmap = new RenderTargetBitmap(1536, 680, 96, 96, PixelFormats.Pbgra32); bitmap.Render(panel); bitmap.Freeze(); return bitmap;
    }

    private static Model3DGroup Joint(Model3DGroup parent, double x, double y, double z, Vector3D axis, out AxisAngleRotation3D rotation)
    {
        rotation = new AxisAngleRotation3D(axis, 0);
        var transform = new Transform3DGroup(); transform.Children.Add(new RotateTransform3D(rotation)); transform.Children.Add(new TranslateTransform3D(x, y, z));
        var group = new Model3DGroup { Transform = transform }; parent.Children.Add(group); return group;
    }
    private static Material Surface(string baseColor, string glow, double emission)
    {
        var a = (Color)ColorConverter.ConvertFromString(baseColor); var b = (Color)ColorConverter.ConvertFromString(glow);
        var group = new MaterialGroup();
        group.Children.Add(new DiffuseMaterial(new SolidColorBrush(a)));
        group.Children.Add(new SpecularMaterial(new SolidColorBrush(b), 70));
        group.Children.Add(new EmissiveMaterial(new SolidColorBrush(Color.FromArgb((byte)(emission * 210), b.R, b.G, b.B))));
        group.Freeze(); return group;
    }
    private static void Box(Model3DGroup g, double x, double y, double z, double w, double h, double d, Material m) => BevelBox(g, x, y, z, w, h, d, 0, m);
    private static void BevelBox(Model3DGroup g, double x, double y, double z, double w, double h, double d, double b, Material m)
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
    private static void Cylinder(Model3DGroup g, double x, double y, double z, double radius, double depth, Material material)
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
    private static void Quad(Model3DGroup g, Point3D a, Point3D b, Point3D c, Point3D d, Material m)
    {
        var mesh = new MeshGeometry3D { Positions = new Point3DCollection { a, b, c, d }, TextureCoordinates = new PointCollection { new(0, 1), new(1, 1), new(1, 0), new(0, 0) }, TriangleIndices = new Int32Collection { 0, 1, 2, 0, 2, 3 } };
        mesh.Freeze(); g.Children.Add(new GeometryModel3D(mesh, m) { BackMaterial = m });
    }
}














