using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace GlitchStich;

public partial class SplashWindow
{
    private GlitchyRig _mascotRig = null!;
    private Model3DGroup? _characterModel;
    private AxisAngleRotation3D _spineBend => _mascotRig._spineBend;
    private AxisAngleRotation3D _spineTwist => _mascotRig._spineTwist;
    private AxisAngleRotation3D _headTilt => _mascotRig._headTilt;
    private AxisAngleRotation3D _headTurn => _mascotRig._headTurn;
    private AxisAngleRotation3D[] _armSwings => _mascotRig._armSwings;
    private AxisAngleRotation3D[] _wrists => _mascotRig._wrists;
    private AxisAngleRotation3D[] _ankles => _mascotRig._ankles;
    private AxisAngleRotation3D[] _coatFlaps => _mascotRig._coatFlaps;
    private void BuildCharacter(Model3DGroup root) => _mascotRig = new GlitchyRig(root);
    private static void Tube(Model3DGroup group, Point3D a, Point3D b, double radius, Material material) => GlitchyRig.Tube(group, a, b, radius, material);
}
