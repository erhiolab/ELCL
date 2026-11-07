using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Threading;

namespace ErhioLab.Craft.Launcher.Views.Launch;

/// <summary>按 Minecraft 皮肤图集绘制可拖动旋转的玩家模型.</summary>
public sealed class SkinModelPreview : Grid
{
    private const double DefaultYaw = -20;
    private const double DefaultPitch = -7;
    private const double DefaultDistance = 54;
    private readonly Model3DGroup _scene = new();
    private readonly AxisAngleRotation3D _yaw = new(new Vector3D(0, 1, 0), DefaultYaw);
    private readonly AxisAngleRotation3D _pitch = new(new Vector3D(1, 0, 0), DefaultPitch);
    private readonly PerspectiveCamera _camera = new(new Point3D(0, -8, DefaultDistance), new Vector3D(0, 0, -DefaultDistance), new Vector3D(0, 1, 0), 42);
    private Point? _dragStart;
    private BitmapSource? _skin;
    private BitmapSource? _cape;
    private bool _slim;
    private bool _previewElytra;
    private readonly DispatcherTimer _idleRotationTimer = new(DispatcherPriority.Background)
    {
        Interval = TimeSpan.FromSeconds(3)
    };
    private bool _autoRotating;
    private long _lastRotationTimestamp;
    private double _autoRotationSpeed;

    public SkinModelPreview()
    {
        _idleRotationTimer.Tick += (_, _) => StartAutoRotation();
        Loaded += (_, _) => RestartIdleRotation();
        Unloaded += (_, _) => PauseAutoRotation();
        IsVisibleChanged += (_, _) => RestartIdleRotation();

        ModelVisual3D visual = new() { Content = _scene };
        Transform3DGroup transforms = new();
        transforms.Children.Add(new RotateTransform3D(_yaw, new Point3D(0, -8, 0)));
        transforms.Children.Add(new RotateTransform3D(_pitch, new Point3D(0, -8, 0)));
        visual.Transform = transforms;

        Viewport3D viewport = new()
        {
            Camera = _camera
        };
        viewport.Children.Add(visual);
        Children.Add(viewport);

        Border input = new() { Background = Brushes.Transparent, Cursor = Cursors.Hand };
        input.PreviewMouseDown += (_, _) => RestartIdleRotation();
        input.MouseLeftButtonDown += (_, e) =>
        {
            PauseAutoRotation();
            _dragStart = e.GetPosition(input);
            input.CaptureMouse();
            e.Handled = true;
        };
        input.MouseMove += (_, e) =>
        {
            if (_dragStart is not Point start || e.LeftButton != MouseButtonState.Pressed) return;
            Point current = e.GetPosition(input);
            _yaw.Angle += (current.X - start.X) * 0.8;
            _pitch.Angle = Math.Clamp(_pitch.Angle + (current.Y - start.Y) * 0.6, -45, 45);
            _dragStart = current;
        };
        input.MouseLeftButtonUp += (_, _) =>
        {
            _dragStart = null;
            input.ReleaseMouseCapture();
            RestartIdleRotation();
        };
        input.LostMouseCapture += (_, _) =>
        {
            _dragStart = null;
            RestartIdleRotation();
        };
        input.MouseWheel += (_, e) =>
        {
            RestartIdleRotation();
            if (_scene.Children.Count == 0) return;
            double distance = Math.Clamp(_camera.Position.Z * Math.Pow(0.88, e.Delta / 120.0), 24, 120);
            _camera.Position = new Point3D(0, -8, distance);
            _camera.LookDirection = new Vector3D(0, 0, -distance);
            e.Handled = true;
        };
        Children.Add(input);
    }

    public void SetAppearance(BitmapSource? skin, bool slim, BitmapSource? cape)
    {
        _skin = skin;
        _slim = slim;
        _cape = cape;
        DrawAppearance();
        RestartIdleRotation();
    }

    public void SetElytraPreview(bool visible)
    {
        if (_previewElytra == visible) return;
        _previewElytra = visible;
        DrawAppearance();
        RestartIdleRotation();
    }

    public void ResetView()
    {
        _dragStart = null;
        if (IsMouseCaptureWithin) Mouse.Capture(null);
        RestartIdleRotation();

        // 选择最近的等价角度, 复位时不绕远路转一整圈.
        double yaw = DefaultYaw + Math.IEEERemainder(_yaw.Angle - DefaultYaw, 360);
        double pitch = _pitch.Angle;
        Point3D position = _camera.Position;
        Vector3D direction = _camera.LookDirection;
        _yaw.Angle = DefaultYaw;
        _pitch.Angle = DefaultPitch;
        _camera.Position = new Point3D(0, -8, DefaultDistance);
        _camera.LookDirection = new Vector3D(0, 0, -DefaultDistance);

        Duration duration = TimeSpan.FromMilliseconds(250);
        CubicEase easing = new() { EasingMode = EasingMode.EaseOut };
        _yaw.BeginAnimation(AxisAngleRotation3D.AngleProperty, new DoubleAnimation(yaw, DefaultYaw, duration)
        {
            EasingFunction = easing, FillBehavior = FillBehavior.Stop
        });
        _pitch.BeginAnimation(AxisAngleRotation3D.AngleProperty, new DoubleAnimation(pitch, DefaultPitch, duration)
        {
            EasingFunction = easing, FillBehavior = FillBehavior.Stop
        });
        _camera.BeginAnimation(ProjectionCamera.PositionProperty, new Point3DAnimation(position, _camera.Position, duration)
        {
            EasingFunction = easing, FillBehavior = FillBehavior.Stop
        });
        _camera.BeginAnimation(ProjectionCamera.LookDirectionProperty, new Vector3DAnimation(direction, _camera.LookDirection, duration)
        {
            EasingFunction = easing, FillBehavior = FillBehavior.Stop
        });
    }

    private void RestartIdleRotation()
    {
        PauseAutoRotation();
        if (IsLoaded && IsVisible && _scene.Children.Count > 0 && _dragStart is null)
            _idleRotationTimer.Start();
    }

    private void PauseAutoRotation()
    {
        _idleRotationTimer.Stop();
        StopViewAnimations();
        if (!_autoRotating) return;
        CompositionTarget.Rendering -= AutoRotation_Rendering;
        _autoRotating = false;
        _autoRotationSpeed = 0;
    }

    private void StopViewAnimations()
    {
        if (_yaw.HasAnimatedProperties)
        {
            double angle = _yaw.Angle;
            _yaw.BeginAnimation(AxisAngleRotation3D.AngleProperty, null);
            _yaw.Angle = angle;
        }
        if (_pitch.HasAnimatedProperties)
        {
            double angle = _pitch.Angle;
            _pitch.BeginAnimation(AxisAngleRotation3D.AngleProperty, null);
            _pitch.Angle = angle;
        }
        if (_camera.HasAnimatedProperties)
        {
            Point3D position = _camera.Position;
            Vector3D direction = _camera.LookDirection;
            _camera.BeginAnimation(ProjectionCamera.PositionProperty, null);
            _camera.BeginAnimation(ProjectionCamera.LookDirectionProperty, null);
            _camera.Position = position;
            _camera.LookDirection = direction;
        }
    }

    private void StartAutoRotation()
    {
        _idleRotationTimer.Stop();
        if (_autoRotating || !IsLoaded || !IsVisible || _scene.Children.Count == 0 || _dragStart is not null) return;
        _lastRotationTimestamp = Stopwatch.GetTimestamp();
        _autoRotationSpeed = 0;
        _autoRotating = true;
        CompositionTarget.Rendering += AutoRotation_Rendering;
    }

    private void AutoRotation_Rendering(object? sender, EventArgs e)
    {
        if (!IsLoaded || !IsVisible || _dragStart is not null)
        {
            PauseAutoRotation();
            return;
        }

        long timestamp = Stopwatch.GetTimestamp();
        // 页面或窗口暂停绘制后, 不补算暂停期间的角度, 避免恢复时突然跳动. 
        double elapsed = Math.Clamp(Stopwatch.GetElapsedTime(_lastRotationTimestamp, timestamp).TotalSeconds, 0, 0.1);
        _lastRotationTimestamp = timestamp;
        _autoRotationSpeed += (12 - _autoRotationSpeed) * (1 - Math.Exp(-6 * elapsed));
        _yaw.Angle = (_yaw.Angle + _autoRotationSpeed * elapsed) % 360;
    }

    private void DrawAppearance()
    {
        BitmapSource? skin = _skin;
        BitmapSource? cape = _cape;
        bool slim = _slim;
        _scene.Children.Clear();
        if (skin is null || skin.PixelWidth < 64 || skin.PixelWidth % 64 != 0 || skin.PixelHeight != skin.PixelWidth && skin.PixelHeight != skin.PixelWidth / 2) return;

        _scene.Children.Add(new AmbientLight(Color.FromRgb(180, 180, 180)));
        _scene.Children.Add(new DirectionalLight(Color.FromRgb(110, 110, 110), new Vector3D(0.3, -0.4, -1)));

        AddBox(skin, 0, 4, 0, 8, 8, 8, 0, 0, 8, 8, 8);
        AddBox(skin, 0, -6, 0, 8, 12, 4, 16, 16, 8, 12, 4);
        AddBox(skin, -1.9, -18, -0.1, 4, 12, 4, 0, 16, 4, 12, 4);
        AddBox(skin, 1.9, -18, -0.1, 4, 12, 4,
            skin.PixelHeight >= skin.PixelWidth ? 16 : 0,
            skin.PixelHeight >= skin.PixelWidth ? 48 : 16, 4, 12, 4);

        int armWidth = slim ? 3 : 4;
        double armX = slim ? 5.5 : 6;
        AddBox(skin, -armX, -6, 0, armWidth, 12, 4, 40, 16, armWidth, 12, 4);
        AddBox(skin, armX, -6, 0, armWidth, 12, 4,
            skin.PixelHeight >= skin.PixelWidth ? 32 : 40,
            skin.PixelHeight >= skin.PixelWidth ? 48 : 16, armWidth, 12, 4);

        AddBox(skin, 0, 4, 0, 9, 9, 9, 32, 0, 8, 8, 8);
        if (skin.PixelHeight >= skin.PixelWidth)
        {
            AddBox(skin, 0, -6, 0, 8.5, 12.5, 4.5, 16, 32, 8, 12, 4);
            AddBox(skin, -1.9, -18, -0.1, 4.5, 12.5, 4.5, 0, 32, 4, 12, 4);
            AddBox(skin, 1.9, -18, -0.1, 4.5, 12.5, 4.5, 0, 48, 4, 12, 4);
            AddBox(skin, -armX, -6, 0, armWidth + 0.5, 12.5, 4.5, 40, 32, armWidth, 12, 4);
            AddBox(skin, armX, -6, 0, armWidth + 0.5, 12.5, 4.5, 48, 48, armWidth, 12, 4);
        }

        if (cape is { PixelWidth: >= 64, PixelHeight: >= 32 } && cape.PixelWidth % 64 == 0 &&
            cape.PixelHeight == cape.PixelWidth / 2)
        {
            if (_previewElytra)
            {
                AddElytraWing(cape, -1);
                AddElytraWing(cape, 1);
            }
            else AddBox(cape, 0, -8, -3.3, 10, 16, 1, 0, 0, 10, 16, 1, reverseFrontAndBack: true, alphaCutout: true);
        }
    }

    private void AddElytraWing(BitmapSource cape, int side)
    {
        // PrismLauncher: Rx(15) * Rz(15) * Rx(1), 镜像翼再左乘 Sx(-1). 
        // Qt 将后续矩阵右乘, WPF 按 Children 顺序作用, 因此这里逆序添加. 
        Transform3DGroup pose = new();
        pose.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(1, 0, 0), 1)));
        pose.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0, 0, 1), 15)));
        pose.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(1, 0, 0), 15)));
        if (side < 0) pose.Children.Add(new ScaleTransform3D(-1, 1, 1));
        AddBox(cape, 0, -13, -2, 12, 22, 4, 22, 0, 10, 20, 2, transform: pose, alphaCutout: true);
    }

    private void AddBox(BitmapSource atlas, double x, double y, double z,
        double width, double height, double depth, int u, int v, int textureWidth, int textureHeight, int textureDepth,
        bool reverseFrontAndBack = false, Transform3D? transform = null, bool alphaCutout = false)
    {
        double left = x - width / 2, right = x + width / 2;
        double top = y + height / 2, bottom = y - height / 2;
        double front = z + depth / 2, back = z - depth / 2;

        AddFace(atlas, u + (reverseFrontAndBack ? textureDepth * 2 + textureWidth : textureDepth),
            v + textureDepth, textureWidth, textureHeight,
            new Point3D(left, top, front), new Point3D(right, top, front),
            new Point3D(right, bottom, front), new Point3D(left, bottom, front), transform, alphaCutout);
        AddFace(atlas, u + (reverseFrontAndBack ? textureDepth : textureDepth * 2 + textureWidth),
            v + textureDepth, textureWidth, textureHeight,
            new Point3D(right, top, back), new Point3D(left, top, back),
            new Point3D(left, bottom, back), new Point3D(right, bottom, back), transform, alphaCutout);
        AddFace(atlas, u + textureDepth + textureWidth, v + textureDepth, textureDepth, textureHeight,
            new Point3D(right, top, front), new Point3D(right, top, back),
                new Point3D(right, bottom, back), new Point3D(right, bottom, front), transform, alphaCutout);
        AddFace(atlas, u, v + textureDepth, textureDepth, textureHeight,
            new Point3D(left, top, back), new Point3D(left, top, front),
                new Point3D(left, bottom, front), new Point3D(left, bottom, back), transform, alphaCutout);
        AddFace(atlas, u + textureDepth, v, textureWidth, textureDepth,
            new Point3D(left, top, back), new Point3D(right, top, back),
                new Point3D(right, top, front), new Point3D(left, top, front), transform, alphaCutout);
        AddFace(atlas, u + textureDepth + textureWidth, v, textureWidth, textureDepth,
            new Point3D(left, bottom, back), new Point3D(right, bottom, back),
                new Point3D(right, bottom, front), new Point3D(left, bottom, front), transform, alphaCutout);
    }

    private void AddFace(BitmapSource atlas, int u, int v, int width, int height,
        Point3D topLeft, Point3D topRight, Point3D bottomRight, Point3D bottomLeft, Transform3D? transform, bool alphaCutout = false)
    {
        int scale = atlas.PixelWidth / 64;
        Int32Rect rect = new(u * scale, v * scale, width * scale, height * scale);
        if (rect.X < 0 || rect.Y < 0 || rect.X + rect.Width > atlas.PixelWidth || rect.Y + rect.Height > atlas.PixelHeight) return;

        BitmapSource texture = new CroppedBitmap(atlas, rect);
        MeshGeometry3D mesh = new()
        {
            Positions = [topLeft, topRight, bottomRight, bottomLeft],
            TextureCoordinates = [new Point(0, 0), new Point(1, 0), new Point(1, 1), new Point(0, 1)],
            TriangleIndices = [0, 1, 2, 0, 2, 3]
        };
        if (alphaCutout)
        {
            (mesh, texture) = CreateCutoutFace(texture, topLeft, topRight, bottomRight, bottomLeft);
            if (mesh.Positions.Count == 0) return;
        }

        BitmapSource pixelated = EnlargeNearest(texture, Math.Max(1, 16 / scale));
        ImageBrush brush = new(pixelated) { Stretch = Stretch.Fill };
        if (alphaCutout)
        {
            // 裁掉透明像素后 UV 的范围可能不足 0..1, 不能再按网格 UV 边界拉伸图片. 
            brush.ViewportUnits = BrushMappingMode.Absolute;
            brush.Viewport = new Rect(0, 0, 1, 1);
        }
        RenderOptions.SetBitmapScalingMode(brush, BitmapScalingMode.NearestNeighbor);
        DiffuseMaterial material = new(brush);
        _scene.Children.Add(new GeometryModel3D(mesh, material) { BackMaterial = material, Transform = transform });
    }

    private static (MeshGeometry3D Mesh, BitmapSource Texture) CreateCutoutFace(BitmapSource texture,
        Point3D topLeft, Point3D topRight, Point3D bottomRight, Point3D bottomLeft)
    {
        // 对应 PL 片元着色器的 alpha < 0.1 discard. WPF 没有 alpha test, 
        // 因此只为可见像素生成网格, 防止透明矩形写入深度后遮住另一片翼. 
        FormatConvertedBitmap converted = new(texture, PixelFormats.Bgra32, null, 0);
        int width = texture.PixelWidth;
        int height = texture.PixelHeight;
        int stride = width * 4;
        byte[] pixels = new byte[stride * height];
        converted.CopyPixels(pixels, stride, 0);
        MeshGeometry3D mesh = new();

        Point3D Position(double u, double v)
        {
            Point3D top = topLeft + (topRight - topLeft) * u;
            Point3D bottom = bottomLeft + (bottomRight - bottomLeft) * u;
            return top + (bottom - top) * v;
        }

        for (int y = 0; y < height; y++)
        {
            int x = 0;
            while (x < width)
            {
                while (x < width && pixels[y * stride + x * 4 + 3] < 26) x++;
                int start = x;
                while (x < width && pixels[y * stride + x * 4 + 3] >= 26) x++;
                if (start == x) continue;

                double u0 = (double)start / width, u1 = (double)x / width;
                double v0 = (double)y / height, v1 = (double)(y + 1) / height;
                int index = mesh.Positions.Count;
                mesh.Positions.Add(Position(u0, v0));
                mesh.Positions.Add(Position(u1, v0));
                mesh.Positions.Add(Position(u1, v1));
                mesh.Positions.Add(Position(u0, v1));
                mesh.TextureCoordinates.Add(new Point(u0, v0));
                mesh.TextureCoordinates.Add(new Point(u1, v0));
                mesh.TextureCoordinates.Add(new Point(u1, v1));
                mesh.TextureCoordinates.Add(new Point(u0, v1));
                mesh.TriangleIndices.Add(index);
                mesh.TriangleIndices.Add(index + 1);
                mesh.TriangleIndices.Add(index + 2);
                mesh.TriangleIndices.Add(index);
                mesh.TriangleIndices.Add(index + 2);
                mesh.TriangleIndices.Add(index + 3);
            }
        }

        // 透明位置已经没有几何体, 剩余纹理使用不透明材质, 避免 WPF 透明面排序. 
        for (int offset = 3; offset < pixels.Length; offset += 4) pixels[offset] = 255;
        BitmapSource opaque = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
        opaque.Freeze();
        mesh.Freeze();
        return (mesh, opaque);
    }

    // WPF 的 3D 贴图仍可能进行线性采样, 先在原始像素之间复制像素块, 避免放大后糊成一片.
    private static BitmapSource EnlargeNearest(BitmapSource source, int factor)
    {
        FormatConvertedBitmap converted = new(source, PixelFormats.Pbgra32, null, 0);
        int sourceStride = source.PixelWidth * 4;
        byte[] input = new byte[sourceStride * source.PixelHeight];
        converted.CopyPixels(input, sourceStride, 0);

        int width = source.PixelWidth * factor;
        int stride = width * 4;
        byte[] output = new byte[stride * source.PixelHeight * factor];
        for (int y = 0; y < source.PixelHeight; y++)
        {
            int row = y * factor * stride;
            for (int x = 0; x < source.PixelWidth; x++)
            for (int repeat = 0; repeat < factor; repeat++) Buffer.BlockCopy(input, y * sourceStride + x * 4, output, row + (x * factor + repeat) * 4, 4);
            for (int repeat = 1; repeat < factor; repeat++) Buffer.BlockCopy(output, row, output, row + repeat * stride, stride);
        }

        BitmapSource result = BitmapSource.Create(width, source.PixelHeight * factor, 96, 96, PixelFormats.Pbgra32, null, output, stride);
        result.Freeze();
        return result;
    }
}
