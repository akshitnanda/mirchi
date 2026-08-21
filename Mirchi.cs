using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Mirchi
{
    internal enum CharmDesign
    {
        NimbuMirchi,
        Nazar,
        Hamsa,
        Omamori,
        Cornicello,
        ChineseKnot
    }

    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Native.SetProcessDPIAware();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MirchiForm());
        }
    }

    internal sealed class MirchiForm : Form
    {
        private const int CanvasWidth = 540;
        private const int CanvasHeight = 270;
        private const float AnchorX = CanvasWidth / 2f;
        private const float AnchorY = 9f;
        private const int ChainPointCount = 12;
        private const float SegmentLength = 14f;
        private const float ChainLength = (ChainPointCount - 1) * SegmentLength;

        private readonly Timer timer;
        private readonly Random random = new Random();
        private readonly ContextMenuStrip menu;
        private readonly ToolStripMenuItem[] designItems = new ToolStripMenuItem[6];
        private readonly PointF[] chain = new PointF[ChainPointCount];
        private readonly PointF[] previousChain = new PointF[ChainPointCount];
        private DateTime lastFrame = DateTime.UtcNow;
        private float idleClock;
        private float elasticScale = 1f;
        private float elasticVelocity;
        private bool draggingCharm;
        private bool draggingAnchor;
        private int draggedNode = ChainPointCount - 1;
        private PointF dragTarget;
        private Point anchorDragOffset;
        private CharmDesign currentDesign = CharmDesign.NimbuMirchi;

        public MirchiForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            ClientSize = new Size(CanvasWidth, CanvasHeight);
            MinimumSize = ClientSize;
            MaximumSize = ClientSize;

            Rectangle area = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(area.Right - (int)AnchorX - 42, area.Top);
            InitializeChain();

            menu = new ContextMenuStrip();
            menu.Items.Add("Give it a wiggle", null, delegate { Kick(); });
            menu.Items.Add("Reset", null, delegate { InitializeChain(); });
            ToolStripMenuItem designMenu = new ToolStripMenuItem("Design");
            AddDesignItem(designMenu, "Nimbu Mirchi · India", CharmDesign.NimbuMirchi);
            AddDesignItem(designMenu, "Nazar · Türkiye", CharmDesign.Nazar);
            AddDesignItem(designMenu, "Hamsa · North Africa / West Asia", CharmDesign.Hamsa);
            AddDesignItem(designMenu, "Omamori · Japan", CharmDesign.Omamori);
            AddDesignItem(designMenu, "Cornicello · Italy", CharmDesign.Cornicello);
            AddDesignItem(designMenu, "Lucky Knot & Coin · China", CharmDesign.ChineseKnot);
            menu.Items.Add(designMenu);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Quit", null, delegate { Close(); });
            ContextMenuStrip = menu;

            MouseDown += HandleMouseDown;
            MouseMove += HandleMouseMove;
            MouseUp += HandleMouseUp;
            MouseDoubleClick += delegate { Kick(); };
            MouseWheel += HandleMouseWheel;

            timer = new Timer();
            timer.Interval = 15;
            timer.Tick += Tick;
            Shown += delegate
            {
                lastFrame = DateTime.UtcNow;
                timer.Start();
                DrawFrame();
            };
        }

        private void AddDesignItem(ToolStripMenuItem parent, string label, CharmDesign design)
        {
            ToolStripMenuItem item = new ToolStripMenuItem(label);
            item.Tag = design;
            item.Checked = design == currentDesign;
            item.Click += delegate { SetDesign((CharmDesign)item.Tag); };
            designItems[(int)design] = item;
            parent.DropDownItems.Add(item);
        }

        private void SetDesign(CharmDesign design)
        {
            currentDesign = design;
            for (int i = 0; i < designItems.Length; i++)
            {
                if (designItems[i] != null) designItems[i].Checked = i == (int)design;
            }
            Kick();
        }

        private void HandleMouseWheel(object sender, MouseEventArgs e)
        {
            int direction = e.Delta < 0 ? 1 : -1;
            int next = ((int)currentDesign + direction + designItems.Length) % designItems.Length;
            SetDesign((CharmDesign)next);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_LAYERED | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE;
                return cp;
            }
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_NCHITTEST)
            {
                long packed = m.LParam.ToInt64();
                Point screenPoint = new Point((short)(packed & 0xffff), (short)((packed >> 16) & 0xffff));
                Point localPoint = PointToClient(screenPoint);
                m.Result = (IntPtr)(IsInteractivePoint(localPoint) ? Native.HTCLIENT : Native.HTTRANSPARENT);
                return;
            }
            base.WndProc(ref m);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            timer.Stop();
            menu.Dispose();
            base.OnFormClosed(e);
        }

        private void Tick(object sender, EventArgs e)
        {
            DateTime now = DateTime.UtcNow;
            float dt = (float)(now - lastFrame).TotalSeconds;
            lastFrame = now;
            if (dt > 0.04f) dt = 0.04f;

            int steps = Math.Max(1, (int)Math.Ceiling(dt / 0.008f));
            float step = dt / steps;
            for (int i = 0; i < steps; i++)
            {
                StepPhysics(step);
            }

            DrawFrame();
        }

        private void StepPhysics(float dt)
        {
            idleClock += dt;

            float desiredElasticScale = 1f;
            if (draggingCharm && draggedNode >= 4)
            {
                float dx = dragTarget.X - AnchorX;
                float dy = dragTarget.Y - AnchorY;
                float pulledDistance = (float)Math.Sqrt(dx * dx + dy * dy);
                float restingDistance = draggedNode * SegmentLength;
                desiredElasticScale = Math.Max(1f, Math.Min(1.48f, pulledDistance / restingDistance));
            }

            // While held, the cord loads smoothly. On release, the stronger under-damped
            // spring converts that stored extension into a visible snap and small rebound.
            float elasticSpring = draggingCharm ? 78f : 145f;
            float elasticDamping = draggingCharm ? 13f : 8.2f;
            float elasticAcceleration = elasticSpring * (desiredElasticScale - elasticScale)
                                      - elasticDamping * elasticVelocity;
            elasticVelocity += elasticAcceleration * dt;
            elasticScale += elasticVelocity * dt;
            elasticScale = Math.Max(0.84f, Math.Min(1.55f, elasticScale));

            // Verlet integration gives the pendant weight without accumulating angular errors.
            for (int i = 1; i < ChainPointCount; i++)
            {
                PointF current = chain[i];
                float depth = i / (float)(ChainPointCount - 1);
                float velocityX = (current.X - previousChain[i].X) * 0.994f;
                float velocityY = (current.Y - previousChain[i].Y) * 0.994f;
                previousChain[i] = current;

                float breeze = (float)(Math.Sin(idleClock * 1.7 + depth * 2.3) * 8.5f * depth);
                chain[i].X += velocityX + breeze * dt * dt;
                chain[i].Y += velocityY + 1050f * dt * dt;
            }

            if (draggingCharm)
            {
                PointF p = chain[draggedNode];
                float pull = 1f - (float)Math.Exp(-18f * dt);
                chain[draggedNode] = new PointF(
                    p.X + (dragTarget.X - p.X) * pull,
                    p.Y + (dragTarget.Y - p.Y) * pull);
            }

            // Repeated distance constraints turn the free particles into a flexible elastic chain.
            for (int pass = 0; pass < 8; pass++)
            {
                chain[0] = new PointF(AnchorX, AnchorY);
                float elasticSegmentLength = SegmentLength * elasticScale;
                for (int i = 0; i < ChainPointCount - 1; i++)
                {
                    PointF a = chain[i];
                    PointF b = chain[i + 1];
                    float dx = b.X - a.X;
                    float dy = b.Y - a.Y;
                    float distance = (float)Math.Sqrt(dx * dx + dy * dy);
                    if (distance < 0.001f) continue;
                    float error = (distance - elasticSegmentLength) / distance;

                    if (i == 0)
                    {
                        b.X -= dx * error;
                        b.Y -= dy * error;
                    }
                    else
                    {
                        float shareA = (draggingCharm && i == draggedNode) ? 0.08f : 0.5f;
                        float shareB = (draggingCharm && i + 1 == draggedNode) ? 0.08f : 0.5f;
                        float total = shareA + shareB;
                        shareA /= total;
                        shareB /= total;
                        a.X += dx * error * shareA;
                        a.Y += dy * error * shareA;
                        b.X -= dx * error * shareB;
                        b.Y -= dy * error * shareB;
                        chain[i] = a;
                    }
                    chain[i + 1] = b;
                }
            }
            chain[0] = new PointF(AnchorX, AnchorY);
        }

        private void Kick()
        {
            float kick = (float)(random.NextDouble() * 10f - 5f);
            if (Math.Abs(kick) < 2.4f) kick = kick < 0 ? -4.2f : 4.2f;
            for (int i = 1; i < ChainPointCount; i++)
            {
                float depth = i / (float)(ChainPointCount - 1);
                previousChain[i].X -= kick * depth * depth;
                previousChain[i].Y += Math.Abs(kick) * 0.08f * depth;
            }
        }

        private void InitializeChain()
        {
            for (int i = 0; i < ChainPointCount; i++)
            {
                PointF p = new PointF(AnchorX, AnchorY + i * SegmentLength);
                chain[i] = p;
                previousChain[i] = p;
            }
            draggingCharm = false;
            draggingAnchor = false;
            elasticScale = 1f;
            elasticVelocity = 0f;
        }

        private void HandleMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;

            PointF p = e.Location;
            float anchorDistance = Distance(p, new PointF(AnchorX, AnchorY));
            if (anchorDistance <= 16f)
            {
                draggingAnchor = true;
                anchorDragOffset = new Point((int)(p.X - AnchorX), (int)(p.Y - AnchorY));
            }
            else
            {
                draggingCharm = true;
                draggedNode = FindNearestChainNode(p);
                dragTarget = ClampDragTarget(p, draggedNode * SegmentLength);
            }
            Capture = true;
        }

        private void HandleMouseMove(object sender, MouseEventArgs e)
        {
            if (draggingAnchor)
            {
                Point screen = Cursor.Position;
                Point oldLocation = Location;
                Point newLocation = new Point(screen.X - (int)AnchorX - anchorDragOffset.X,
                                              screen.Y - (int)AnchorY - anchorDragOffset.Y);
                Location = newLocation;

                // Preserve some world-space inertia so the pendant trails a moving hook.
                float shiftX = (newLocation.X - oldLocation.X) * 0.72f;
                float shiftY = (newLocation.Y - oldLocation.Y) * 0.72f;
                for (int i = 1; i < ChainPointCount; i++)
                {
                    chain[i].X -= shiftX;
                    chain[i].Y -= shiftY;
                    previousChain[i].X -= shiftX;
                    previousChain[i].Y -= shiftY;
                }
            }
            else if (draggingCharm)
            {
                dragTarget = ClampDragTarget(e.Location, draggedNode * SegmentLength);
            }
        }

        private void HandleMouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            draggingCharm = false;
            draggingAnchor = false;
            Capture = false;
        }

        private int FindNearestChainNode(PointF point)
        {
            int nearest = 1;
            float best = float.MaxValue;
            for (int i = 1; i < ChainPointCount; i++)
            {
                float distance = Distance(point, chain[i]);
                if (distance < best)
                {
                    best = distance;
                    nearest = i;
                }
            }
            return nearest;
        }

        private static PointF ClampDragTarget(PointF point, float radius)
        {
            float dx = point.X - AnchorX;
            float dy = point.Y - AnchorY;
            float distance = (float)Math.Sqrt(dx * dx + dy * dy);
            float maximum = Math.Max(SegmentLength, radius * 1.52f);
            if (distance > maximum)
            {
                float scale = maximum / distance;
                dx *= scale;
                dy *= scale;
            }
            return new PointF(AnchorX + dx, AnchorY + dy);
        }

        private static float Distance(PointF a, PointF b)
        {
            float dx = a.X - b.X;
            float dy = a.Y - b.Y;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }

        private bool IsInteractivePoint(PointF point)
        {
            PointF anchor = new PointF(AnchorX, AnchorY);
            if (Distance(point, anchor) <= 15f) return true;

            for (int i = 0; i < ChainPointCount - 1; i++)
            {
                float hitWidth = i < 2 ? 7f : 28f;
                if (DistanceToSegment(point, chain[i], chain[i + 1]) <= hitWidth) return true;
            }
            return false;
        }

        private static float DistanceToSegment(PointF point, PointF start, PointF end)
        {
            float dx = end.X - start.X;
            float dy = end.Y - start.Y;
            float lengthSquared = dx * dx + dy * dy;
            if (lengthSquared < 0.001f) return Distance(point, start);
            float t = ((point.X - start.X) * dx + (point.Y - start.Y) * dy) / lengthSquared;
            t = Math.Max(0f, Math.Min(1f, t));
            return Distance(point, new PointF(start.X + t * dx, start.Y + t * dy));
        }

        private void DrawFrame()
        {
            using (Bitmap bitmap = new Bitmap(CanvasWidth, CanvasHeight, PixelFormat.Format32bppArgb))
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.CompositingQuality = CompositingQuality.HighQuality;

                DrawCharm(g);
                Present(bitmap);
            }
        }

        private void DrawCharm(Graphics g)
        {
            Color threadColor = GetThreadColor();
            using (GraphicsPath threadPath = new GraphicsPath())
            using (Pen threadShadow = new Pen(Color.FromArgb(42, 0, 0, 0), 4.2f))
            using (Pen thread = new Pen(threadColor, 2.05f))
            {
                threadPath.AddLines(chain);
                threadShadow.StartCap = threadShadow.EndCap = LineCap.Round;
                threadShadow.LineJoin = LineJoin.Round;
                thread.StartCap = thread.EndCap = LineCap.Round;
                thread.LineJoin = LineJoin.Round;
                GraphicsState shadowState = g.Save();
                g.TranslateTransform(1.2f, 1.2f);
                g.DrawPath(threadShadow, threadPath);
                g.Restore(shadowState);
                g.DrawPath(thread, threadPath);
            }

            DrawCurrentDesign(g);

            // The pin/loop remains fixed while the charm swings.
            using (SolidBrush pinShadow = new SolidBrush(Color.FromArgb(55, 0, 0, 0)))
            using (SolidBrush pin = new SolidBrush(threadColor))
            using (Pen loop = new Pen(Color.FromArgb(235, threadColor.R, threadColor.G, threadColor.B), 2f))
            {
                g.FillEllipse(pinShadow, AnchorX - 4f, AnchorY - 3f, 10f, 10f);
                g.FillEllipse(pin, AnchorX - 4f, AnchorY - 4f, 8f, 8f);
                g.DrawEllipse(loop, AnchorX - 7f, AnchorY - 1f, 14f, 9f);
            }
        }

        private Color GetThreadColor()
        {
            switch (currentDesign)
            {
                case CharmDesign.Nazar: return Color.FromArgb(245, 32, 94, 176);
                case CharmDesign.Hamsa: return Color.FromArgb(245, 37, 126, 132);
                case CharmDesign.Omamori: return Color.FromArgb(245, 225, 72, 92);
                case CharmDesign.Cornicello: return Color.FromArgb(245, 175, 126, 44);
                case CharmDesign.ChineseKnot: return Color.FromArgb(245, 202, 35, 34);
                default: return Color.FromArgb(245, 203, 35, 32);
            }
        }

        private void DrawCurrentDesign(Graphics g)
        {
            switch (currentDesign)
            {
                case CharmDesign.Nazar: DrawNazarDesign(g); break;
                case CharmDesign.Hamsa: DrawHamsaDesign(g); break;
                case CharmDesign.Omamori: DrawOmamoriDesign(g); break;
                case CharmDesign.Cornicello: DrawCornicelloDesign(g); break;
                case CharmDesign.ChineseKnot: DrawChineseKnotDesign(g); break;
                default: DrawNimbuMirchiDesign(g); break;
            }
        }

        private void DrawNimbuMirchiDesign(Graphics g)
        {
            DrawKnotOnChain(g, 37f);
            DrawChilliOnChain(g, 49f, 0.97f, false, false, 0.32f);
            DrawChilliOnChain(g, 64f, 0.92f, true, true, -0.40f);
            DrawLemonOnChain(g, 92f, 1.72f, -2f);
            DrawChilliOnChain(g, 121f, 0.96f, true, false, -0.48f);
            DrawChilliOnChain(g, 136f, 0.91f, false, true, 0.56f);
            DrawBottomBeadOnChain(g, 151f);
        }

        private void DrawNazarDesign(Graphics g)
        {
            DrawBeadOnChain(g, 39f, 4.5f, Color.FromArgb(255, 84, 194, 226), Color.FromArgb(255, 21, 79, 159));
            DrawBeadOnChain(g, 53f, 5.5f, Color.FromArgb(255, 65, 135, 224), Color.FromArgb(255, 24, 43, 132));
            GraphicsState state = PlaceOnChain(g, 88f);
            DrawNazarMedallion(g);
            g.Restore(state);
            DrawBeadOnChain(g, 122f, 5.2f, Color.FromArgb(255, 63, 158, 220), Color.FromArgb(255, 20, 49, 139));
            DrawBeadOnChain(g, 138f, 4.2f, Color.FromArgb(255, 111, 213, 229), Color.FromArgb(255, 27, 92, 166));
            DrawBottomBeadOnChain(g, 151f);
        }

        private void DrawHamsaDesign(Graphics g)
        {
            DrawBeadOnChain(g, 40f, 4.5f, Color.FromArgb(255, 92, 210, 202), Color.FromArgb(255, 20, 108, 121));
            DrawBeadOnChain(g, 54f, 3.8f, Color.FromArgb(255, 245, 207, 90), Color.FromArgb(255, 159, 105, 26));
            GraphicsState state = PlaceOnChain(g, 94f);
            DrawHamsaHand(g);
            g.Restore(state);
            DrawBeadOnChain(g, 132f, 4.8f, Color.FromArgb(255, 79, 190, 188), Color.FromArgb(255, 17, 102, 118));
            DrawBottomBeadOnChain(g, 151f);
        }

        private void DrawOmamoriDesign(Graphics g)
        {
            DrawBeadOnChain(g, 39f, 4.2f, Color.FromArgb(255, 244, 180, 194), Color.FromArgb(255, 182, 48, 80));
            GraphicsState knotState = PlaceOnChain(g, 55f);
            DrawDecorativeKnot(g, Color.FromArgb(255, 248, 231, 200), Color.FromArgb(255, 197, 58, 82));
            g.Restore(knotState);
            GraphicsState pouchState = PlaceOnChain(g, 98f);
            DrawOmamoriPouch(g);
            g.Restore(pouchState);
            DrawBeadOnChain(g, 139f, 4.5f, Color.FromArgb(255, 247, 190, 202), Color.FromArgb(255, 171, 39, 72));
            DrawBottomBeadOnChain(g, 151f);
        }

        private void DrawCornicelloDesign(Graphics g)
        {
            DrawBeadOnChain(g, 40f, 4.4f, Color.FromArgb(255, 247, 211, 106), Color.FromArgb(255, 158, 101, 25));
            DrawBeadOnChain(g, 55f, 5.2f, Color.FromArgb(255, 211, 67, 55), Color.FromArgb(255, 123, 25, 31));
            GraphicsState state = PlaceOnChain(g, 101f);
            DrawCornicello(g);
            g.Restore(state);
            DrawBeadOnChain(g, 143f, 4.4f, Color.FromArgb(255, 245, 204, 87), Color.FromArgb(255, 151, 91, 23));
            DrawBottomBeadOnChain(g, 152f);
        }

        private void DrawChineseKnotDesign(Graphics g)
        {
            DrawBeadOnChain(g, 39f, 4.5f, Color.FromArgb(255, 235, 72, 62), Color.FromArgb(255, 153, 24, 29));
            GraphicsState knotState = PlaceOnChain(g, 72f);
            DrawChineseKnot(g);
            g.Restore(knotState);
            GraphicsState coinState = PlaceOnChain(g, 113f);
            DrawLuckyCoin(g);
            g.Restore(coinState);
            GraphicsState tasselState = PlaceOnChain(g, 143f);
            DrawTassel(g);
            g.Restore(tasselState);
        }

        private void DrawBeadOnChain(Graphics g, float distance, float radius, Color light, Color dark)
        {
            GraphicsState state = PlaceOnChain(g, distance);
            using (SolidBrush shadow = new SolidBrush(Color.FromArgb(45, 0, 0, 0)))
            using (LinearGradientBrush fill = new LinearGradientBrush(
                new RectangleF(-radius, -radius, radius * 2f, radius * 2f), light, dark, 65f))
            using (Pen edge = new Pen(Color.FromArgb(155, dark), 0.8f))
            using (SolidBrush glint = new SolidBrush(Color.FromArgb(125, 255, 255, 255)))
            {
                g.FillEllipse(shadow, -radius + 1f, -radius + 1.3f, radius * 2f, radius * 2f);
                g.FillEllipse(fill, -radius, -radius, radius * 2f, radius * 2f);
                g.DrawEllipse(edge, -radius, -radius, radius * 2f, radius * 2f);
                g.FillEllipse(glint, -radius * 0.48f, -radius * 0.58f, radius * 0.55f, radius * 0.42f);
            }
            g.Restore(state);
        }

        private static void DrawNazarMedallion(Graphics g)
        {
            using (SolidBrush shadow = new SolidBrush(Color.FromArgb(48, 0, 0, 0)))
            using (LinearGradientBrush blue = new LinearGradientBrush(new RectangleF(-23f, -22f, 46f, 46f),
                Color.FromArgb(255, 45, 145, 224), Color.FromArgb(255, 15, 49, 145), 70f))
            using (SolidBrush cyan = new SolidBrush(Color.FromArgb(255, 73, 207, 228)))
            using (SolidBrush white = new SolidBrush(Color.FromArgb(255, 246, 246, 224)))
            using (SolidBrush iris = new SolidBrush(Color.FromArgb(255, 38, 101, 198)))
            using (SolidBrush pupil = new SolidBrush(Color.FromArgb(255, 13, 27, 55)))
            using (Pen rim = new Pen(Color.FromArgb(225, 9, 46, 125), 1.3f))
            using (SolidBrush glint = new SolidBrush(Color.FromArgb(185, 255, 255, 255)))
            {
                g.FillEllipse(shadow, -22f, -20f, 47f, 47f);
                g.FillEllipse(blue, -23f, -23f, 46f, 46f);
                g.DrawEllipse(rim, -23f, -23f, 46f, 46f);
                g.FillEllipse(cyan, -17f, -17f, 34f, 34f);
                g.FillEllipse(white, -12.5f, -12.5f, 25f, 25f);
                g.FillEllipse(iris, -8.5f, -8.5f, 17f, 17f);
                g.FillEllipse(pupil, -4.8f, -4.8f, 9.6f, 9.6f);
                g.FillEllipse(glint, -3.2f, -3.5f, 3.1f, 3.1f);
            }
        }

        private static void DrawHamsaHand(Graphics g)
        {
            Color edgeColor = Color.FromArgb(225, 112, 79, 20);
            Color handColor = Color.FromArgb(255, 232, 188, 75);
            using (SolidBrush shadow = new SolidBrush(Color.FromArgb(45, 0, 0, 0)))
            using (SolidBrush palm = new SolidBrush(handColor))
            using (Pen fingerEdge = new Pen(edgeColor, 9.5f))
            using (Pen finger = new Pen(handColor, 7f))
            using (Pen outline = new Pen(edgeColor, 1.1f))
            {
                fingerEdge.StartCap = fingerEdge.EndCap = LineCap.Round;
                finger.StartCap = finger.EndCap = LineCap.Round;
                g.FillEllipse(shadow, -18f, -11f, 38f, 43f);
                float[] xs = { -10f, -3.5f, 3.5f, 10f };
                float[] tops = { -17f, -26f, -28f, -19f };
                for (int i = 0; i < xs.Length; i++)
                {
                    g.DrawLine(fingerEdge, xs[i], 0f, xs[i], tops[i]);
                    g.DrawLine(finger, xs[i], 0f, xs[i], tops[i]);
                }
                g.DrawLine(fingerEdge, -13f, 4f, -23f, -7f);
                g.DrawLine(finger, -13f, 4f, -23f, -7f);
                g.FillEllipse(palm, -16f, -9f, 32f, 35f);
                g.DrawEllipse(outline, -16f, -9f, 32f, 35f);
                g.FillRectangle(palm, -9f, 18f, 18f, 14f);
                g.DrawLine(outline, -9f, 30f, 9f, 30f);
            }
            using (SolidBrush eyeWhite = new SolidBrush(Color.FromArgb(250, 249, 243, 210)))
            using (SolidBrush iris = new SolidBrush(Color.FromArgb(255, 45, 147, 177)))
            using (SolidBrush pupil = new SolidBrush(Color.FromArgb(255, 23, 51, 63)))
            using (Pen eyeEdge = new Pen(Color.FromArgb(230, 89, 61, 19), 1.1f))
            {
                g.FillEllipse(eyeWhite, -10f, -2f, 20f, 10f);
                g.DrawEllipse(eyeEdge, -10f, -2f, 20f, 10f);
                g.FillEllipse(iris, -4.5f, -2f, 9f, 10f);
                g.FillEllipse(pupil, -2f, 0.5f, 4f, 5f);
            }
        }

        private static void DrawDecorativeKnot(Graphics g, Color light, Color dark)
        {
            using (Pen shadow = new Pen(Color.FromArgb(42, 0, 0, 0), 5.5f))
            using (Pen cord = new Pen(light, 3.5f))
            using (SolidBrush center = new SolidBrush(dark))
            {
                shadow.StartCap = shadow.EndCap = cord.StartCap = cord.EndCap = LineCap.Round;
                g.DrawEllipse(shadow, -11f, -6f, 11f, 12f);
                g.DrawEllipse(shadow, 0f, -6f, 11f, 12f);
                g.DrawEllipse(cord, -11f, -6f, 11f, 12f);
                g.DrawEllipse(cord, 0f, -6f, 11f, 12f);
                g.FillEllipse(center, -4f, -4f, 8f, 8f);
            }
        }

        private static void DrawOmamoriPouch(Graphics g)
        {
            using (GraphicsPath pouch = new GraphicsPath())
            {
                pouch.AddBezier(-20f, -25f, -11f, -30f, 11f, -30f, 20f, -25f);
                pouch.AddLine(20f, -25f, 20f, 24f);
                pouch.AddBezier(20f, 24f, 12f, 29f, -12f, 29f, -20f, 24f);
                pouch.CloseFigure();
                using (SolidBrush shadow = new SolidBrush(Color.FromArgb(48, 0, 0, 0)))
                using (LinearGradientBrush fill = new LinearGradientBrush(new RectangleF(-21f, -29f, 42f, 58f),
                    Color.FromArgb(255, 241, 126, 151), Color.FromArgb(255, 177, 42, 78), 72f))
                using (Pen gold = new Pen(Color.FromArgb(245, 240, 194, 92), 1.5f))
                using (Pen weave = new Pen(Color.FromArgb(95, 255, 231, 197), 0.75f))
                using (SolidBrush flower = new SolidBrush(Color.FromArgb(180, 255, 220, 193)))
                {
                    GraphicsState shadowState = g.Save();
                    g.TranslateTransform(1.6f, 2f);
                    g.FillPath(shadow, pouch);
                    g.Restore(shadowState);
                    g.FillPath(fill, pouch);
                    g.DrawPath(gold, pouch);
                    g.DrawLine(gold, -18f, -18f, 18f, -18f);
                    for (int x = -14; x <= 14; x += 7) g.DrawLine(weave, x, -16f, x, 23f);
                    g.FillEllipse(flower, -7f, -3f, 6f, 6f);
                    g.FillEllipse(flower, 1f, -3f, 6f, 6f);
                    g.FillEllipse(flower, -3f, -7f, 6f, 6f);
                    g.FillEllipse(flower, -3f, 1f, 6f, 6f);
                    g.FillEllipse(flower, -3f, -3f, 6f, 6f);
                }
            }
        }

        private static void DrawCornicello(Graphics g)
        {
            using (GraphicsPath horn = new GraphicsPath())
            {
                horn.AddBezier(-8f, -34f, 5f, -36f, 13f, -27f, 12f, -16f);
                horn.AddBezier(12f, -16f, 11f, 1f, 2f, 17f, -13f, 29f);
                horn.AddBezier(-13f, 29f, -18f, 33f, -16f, 36f, -9f, 32f);
                horn.AddBezier(-9f, 32f, 10f, 21f, 21f, 3f, 21f, -14f);
                horn.AddBezier(21f, -14f, 21f, -30f, 8f, -40f, -8f, -34f);
                horn.CloseFigure();
                using (SolidBrush shadow = new SolidBrush(Color.FromArgb(48, 0, 0, 0)))
                using (LinearGradientBrush fill = new LinearGradientBrush(new RectangleF(-18f, -39f, 40f, 76f),
                    Color.FromArgb(255, 244, 69, 48), Color.FromArgb(255, 146, 22, 28), 32f))
                using (Pen edge = new Pen(Color.FromArgb(235, 113, 18, 25), 1.2f))
                using (Pen shine = new Pen(Color.FromArgb(125, 255, 177, 133), 1.4f))
                {
                    GraphicsState shadowState = g.Save();
                    g.TranslateTransform(1.7f, 2f);
                    g.FillPath(shadow, horn);
                    g.Restore(shadowState);
                    g.FillPath(fill, horn);
                    g.DrawPath(edge, horn);
                    g.DrawBezier(shine, -3f, -30f, 7f, -22f, 7f, -4f, -2f, 15f);
                }
            }
            using (LinearGradientBrush cap = new LinearGradientBrush(new RectangleF(-10f, -39f, 20f, 9f),
                Color.FromArgb(255, 250, 216, 116), Color.FromArgb(255, 151, 94, 20), 80f))
            using (Pen edge = new Pen(Color.FromArgb(225, 122, 75, 17), 1f))
            {
                g.FillEllipse(cap, -10f, -39f, 20f, 9f);
                g.DrawEllipse(edge, -10f, -39f, 20f, 9f);
            }
        }

        private static void DrawChineseKnot(Graphics g)
        {
            using (Pen shadow = new Pen(Color.FromArgb(48, 0, 0, 0), 7f))
            using (Pen cord = new Pen(Color.FromArgb(255, 214, 38, 35), 4.5f))
            using (Pen glint = new Pen(Color.FromArgb(95, 255, 162, 119), 1f))
            {
                shadow.StartCap = shadow.EndCap = cord.StartCap = cord.EndCap = LineCap.Round;
                g.DrawBezier(shadow, 0f, -26f, -25f, -24f, -25f, -5f, -10f, -7f);
                g.DrawBezier(shadow, 0f, -26f, 25f, -24f, 25f, -5f, 10f, -7f);
                g.DrawPolygon(shadow, new PointF[] { new PointF(0f, -19f), new PointF(16f, 0f), new PointF(0f, 19f), new PointF(-16f, 0f), new PointF(0f, -19f) });
                g.DrawBezier(shadow, -10f, 7f, -23f, 7f, -22f, 25f, -5f, 17f);
                g.DrawBezier(shadow, 10f, 7f, 23f, 7f, 22f, 25f, 5f, 17f);
                g.DrawBezier(cord, 0f, -26f, -25f, -24f, -25f, -5f, -10f, -7f);
                g.DrawBezier(cord, 0f, -26f, 25f, -24f, 25f, -5f, 10f, -7f);
                g.DrawPolygon(cord, new PointF[] { new PointF(0f, -19f), new PointF(16f, 0f), new PointF(0f, 19f), new PointF(-16f, 0f), new PointF(0f, -19f) });
                g.DrawBezier(cord, -10f, 7f, -23f, 7f, -22f, 25f, -5f, 17f);
                g.DrawBezier(cord, 10f, 7f, 23f, 7f, 22f, 25f, 5f, 17f);
                g.DrawLine(glint, -7f, -12f, 7f, 5f);
            }
        }

        private static void DrawLuckyCoin(Graphics g)
        {
            using (SolidBrush shadow = new SolidBrush(Color.FromArgb(48, 0, 0, 0)))
            using (LinearGradientBrush gold = new LinearGradientBrush(new RectangleF(-18f, -18f, 36f, 36f),
                Color.FromArgb(255, 250, 213, 100), Color.FromArgb(255, 163, 100, 23), 65f))
            using (Pen rim = new Pen(Color.FromArgb(235, 130, 78, 18), 1.2f))
            using (SolidBrush hole = new SolidBrush(Color.FromArgb(255, 124, 67, 18)))
            using (Pen inner = new Pen(Color.FromArgb(210, 255, 228, 138), 0.9f))
            {
                g.FillEllipse(shadow, -17f, -16f, 37f, 37f);
                g.FillEllipse(gold, -18f, -18f, 36f, 36f);
                g.DrawEllipse(rim, -18f, -18f, 36f, 36f);
                g.FillRectangle(hole, -5f, -5f, 10f, 10f);
                g.DrawRectangle(inner, -6.5f, -6.5f, 13f, 13f);
                g.DrawArc(inner, -14f, -14f, 28f, 28f, 205f, 130f);
            }
        }

        private static void DrawTassel(Graphics g)
        {
            using (SolidBrush cap = new SolidBrush(Color.FromArgb(255, 171, 28, 30)))
            using (Pen tassel = new Pen(Color.FromArgb(225, 213, 41, 38), 1.5f))
            using (Pen glint = new Pen(Color.FromArgb(105, 255, 143, 108), 0.65f))
            {
                tassel.StartCap = tassel.EndCap = LineCap.Round;
                g.FillRectangle(cap, -7f, -4f, 14f, 8f);
                for (int i = -6; i <= 6; i += 2)
                {
                    g.DrawBezier(tassel, i, 3f, i - 1f, 9f, i + 1f, 15f, i * 0.85f, 21f);
                }
                g.DrawLine(glint, -3f, 5f, -3f, 18f);
            }
        }

        private void DrawKnotOnChain(Graphics g, float distance)
        {
            GraphicsState state = PlaceOnChain(g, distance);
            DrawKnot(g, 0f);
            g.Restore(state);
        }

        private void DrawLemonOnChain(Graphics g, float distance, float scale, float rotation)
        {
            GraphicsState state = PlaceOnChain(g, distance);
            int node = Math.Max(1, Math.Min(ChainPointCount - 1, (int)Math.Round(distance / SegmentLength)));
            float motionX = chain[node].X - previousChain[node].X;
            float motionY = chain[node].Y - previousChain[node].Y;
            float gaze = Math.Max(-1.5f, Math.Min(1.5f, motionX * 0.55f));
            if (draggingCharm)
            {
                gaze += Math.Max(-0.8f, Math.Min(0.8f, (dragTarget.X - chain[node].X) * 0.018f));
            }
            float energy = Math.Min(1f, (float)Math.Sqrt(motionX * motionX + motionY * motionY) / 5f);
            float blinkClock = idleClock % 4.6f;
            bool blinking = !draggingCharm && blinkClock > 4.43f;
            bool surprised = elasticScale > 1.13f;
            DrawLemon(g, 0f, scale, rotation, gaze, energy, blinking, surprised);
            g.Restore(state);
        }

        private void DrawChilliOnChain(Graphics g, float distance, float scale, bool flip, bool red, float motionFactor)
        {
            GraphicsState state = PlaceOnChain(g, distance);
            float motion = GetMotionTwist(distance) * motionFactor;
            DrawHorizontalChilli(g, 0f, scale, flip, red, motion);
            g.Restore(state);
        }

        private void DrawBottomBeadOnChain(Graphics g, float distance)
        {
            GraphicsState state = PlaceOnChain(g, distance);
            DrawBottomBead(g, 0f);
            g.Restore(state);
        }

        private GraphicsState PlaceOnChain(Graphics g, float distance)
        {
            PointF point;
            float rotation;
            GetChainFrame(distance, out point, out rotation);
            GraphicsState state = g.Save();
            g.TranslateTransform(point.X, point.Y);
            g.RotateTransform(rotation);
            return state;
        }

        private void GetChainFrame(float distance, out PointF point, out float rotation)
        {
            float clamped = Math.Max(0f, Math.Min(ChainLength, distance));
            int segment = Math.Min(ChainPointCount - 2, (int)(clamped / SegmentLength));
            float t = (clamped - segment * SegmentLength) / SegmentLength;
            PointF a = chain[segment];
            PointF b = chain[segment + 1];
            point = new PointF(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);

            int before = Math.Max(0, segment - 1);
            int after = Math.Min(ChainPointCount - 1, segment + 2);
            float dx = chain[after].X - chain[before].X;
            float dy = chain[after].Y - chain[before].Y;
            rotation = -(float)Math.Atan2(dx, dy) * 180f / (float)Math.PI;
        }

        private float GetMotionTwist(float distance)
        {
            int index = Math.Max(1, Math.Min(ChainPointCount - 1, (int)Math.Round(distance / SegmentLength)));
            float horizontalSpeed = chain[index].X - previousChain[index].X;
            return Math.Max(-7f, Math.Min(7f, horizontalSpeed * 1.65f));
        }

        private static void DrawKnot(Graphics g, float y)
        {
            using (SolidBrush shadow = new SolidBrush(Color.FromArgb(42, 0, 0, 0)))
            using (SolidBrush knot = new SolidBrush(Color.FromArgb(255, 184, 29, 26)))
            {
                g.FillEllipse(shadow, -5f, y - 4f, 11f, 9f);
                g.FillEllipse(knot, -5f, y - 5f, 10f, 9f);
            }
        }

        private static void DrawLemon(Graphics g, float y, float scale, float rotation,
                                      float gaze, float energy, bool blinking, bool surprised)
        {
            GraphicsState state = g.Save();
            g.TranslateTransform(0f, y);
            g.RotateTransform(rotation);
            g.ScaleTransform(scale, scale);

            using (GraphicsPath leaf = new GraphicsPath())
            {
                leaf.AddBezier(2f, -11f, 7f, -18f, 15f, -19f, 18f, -14f);
                leaf.AddBezier(18f, -14f, 14f, -9f, 7f, -8f, 2f, -11f);
                leaf.CloseFigure();
                using (LinearGradientBrush leafFill = new LinearGradientBrush(new RectangleF(2f, -19f, 17f, 12f),
                    Color.FromArgb(255, 94, 177, 66), Color.FromArgb(255, 24, 105, 50), 30f))
                using (Pen leafEdge = new Pen(Color.FromArgb(220, 19, 85, 42), 0.85f))
                using (Pen vein = new Pen(Color.FromArgb(145, 210, 239, 135), 0.55f))
                {
                    g.FillPath(leafFill, leaf);
                    g.DrawPath(leafEdge, leaf);
                    g.DrawBezier(vein, 4f, -11f, 8f, -13f, 12f, -15f, 16f, -15f);
                }
            }

            using (Pen stem = new Pen(Color.FromArgb(240, 47, 103, 42), 1.5f))
            {
                stem.StartCap = stem.EndCap = LineCap.Round;
                g.DrawBezier(stem, 0f, -12f, 2f, -15f, 4f, -17f, 7f, -18f);
            }

            using (SolidBrush shadow = new SolidBrush(Color.FromArgb(38, 0, 0, 0)))
            {
                g.FillEllipse(shadow, -14f, -11f, 31f, 34f);
            }

            using (GraphicsPath fruit = new GraphicsPath())
            {
                fruit.AddBezier(0f, -13f, 8f, -14f, 13f, -9f, 14f, -2f);
                fruit.AddBezier(14f, -2f, 16f, 6f, 10f, 13f, 2f, 18f);
                fruit.AddBezier(2f, 18f, 1f, 20f, -1f, 20f, -2f, 18f);
                fruit.AddBezier(-2f, 18f, -10f, 14f, -15f, 7f, -14f, 0f);
                fruit.AddBezier(-14f, 0f, -15f, -7f, -9f, -13f, 0f, -13f);
                fruit.CloseFigure();
                using (LinearGradientBrush fill = new LinearGradientBrush(new RectangleF(-16f, -14f, 32f, 35f),
                    Color.FromArgb(255, 255, 239, 78), Color.FromArgb(255, 224, 159, 17), 72f))
                using (Pen edge = new Pen(Color.FromArgb(235, 158, 108, 14), 1.05f))
                {
                    g.FillPath(fill, fruit);
                    GraphicsState clipped = g.Save();
                    g.SetClip(fruit);
                    using (SolidBrush shade = new SolidBrush(Color.FromArgb(58, 157, 100, 7)))
                    using (SolidBrush glow = new SolidBrush(Color.FromArgb(105, 255, 255, 218)))
                    {
                        g.FillEllipse(shade, 4f, -10f, 16f, 31f);
                        g.FillEllipse(glow, -11f, -9f, 8f, 17f);
                    }
                    g.Restore(clipped);
                    g.DrawPath(edge, fruit);
                }
                using (SolidBrush pore = new SolidBrush(Color.FromArgb(75, 139, 92, 8)))
                {
                    g.FillEllipse(pore, 4f, -4f, 1.5f, 1.5f);
                    g.FillEllipse(pore, 8f, 3f, 1.2f, 1.2f);
                    g.FillEllipse(pore, 5f, 10f, 1.4f, 1.4f);
                    g.FillEllipse(pore, -9f, 8f, 1.2f, 1.2f);
                    g.FillEllipse(pore, -7f, -6f, 1.1f, 1.1f);
                }
            }
            DrawLemonFace(g, gaze, energy, blinking, surprised);
            g.Restore(state);
        }

        private static void DrawLemonFace(Graphics g, float gaze, float energy, bool blinking, bool surprised)
        {
            Color inkColor = Color.FromArgb(232, 74, 49, 31);
            using (SolidBrush bindi = new SolidBrush(Color.FromArgb(225, 181, 32, 40)))
            using (SolidBrush blush = new SolidBrush(Color.FromArgb(62 + (int)(energy * 45f), 235, 92, 92)))
            using (SolidBrush eyeWhite = new SolidBrush(Color.FromArgb(220, 255, 251, 218)))
            using (SolidBrush pupil = new SolidBrush(inkColor))
            using (Pen smile = new Pen(Color.FromArgb(220, 163, 54, 56), 0.8f))
            using (Pen feature = new Pen(inkColor, 0.78f))
            {
                feature.StartCap = feature.EndCap = LineCap.Round;
                smile.StartCap = smile.EndCap = LineCap.Round;

                // Fine, asymmetric ink lines keep the expressions illustrated rather than emoji-like.
                if (blinking)
                {
                    g.DrawBezier(feature, -7.1f, 0.6f, -5.8f, 1.8f, -3.7f, 1.8f, -2.5f, 0.5f);
                    g.DrawBezier(feature, 2.4f, 0.5f, 3.7f, 1.8f, 5.8f, 1.8f, 7.1f, 0.5f);
                }
                else
                {
                    using (GraphicsPath leftEye = MakeAlmond(-4.9f, 0.6f, surprised ? 2.2f : 1.7f))
                    using (GraphicsPath rightEye = MakeAlmond(4.9f, 0.6f, surprised ? 2.2f : 1.7f))
                    {
                        g.FillPath(eyeWhite, leftEye);
                        g.FillPath(eyeWhite, rightEye);
                        g.DrawPath(feature, leftEye);
                        g.DrawPath(feature, rightEye);
                    }
                    float pupilY = surprised ? -0.05f : 0.15f;
                    g.FillEllipse(pupil, -5.65f + gaze, pupilY, 1.5f, surprised ? 2.1f : 1.8f);
                    g.FillEllipse(pupil, 4.15f + gaze, pupilY, 1.5f, surprised ? 2.1f : 1.8f);
                    g.DrawLine(feature, -7.0f, -0.3f, -7.8f, -1.2f);
                    g.DrawLine(feature, 6.9f, -0.3f, 7.8f, -1.2f);
                }

                g.FillEllipse(bindi, -0.85f, -5.0f, 1.7f, 1.7f);
                g.DrawBezier(feature, 0.0f, 1.6f, -0.8f, 2.8f, -0.8f, 3.7f, 0.15f, 3.9f);
                g.FillEllipse(blush, -8.8f, 4.0f, 4.8f, 2.8f);
                g.FillEllipse(blush, 4.0f, 4.0f, 4.8f, 2.8f);
                if (surprised)
                {
                    g.DrawEllipse(smile, -1.45f, 6.1f, 2.9f, 3.8f);
                }
                else
                {
                    float lift = 8.0f + energy * 1.0f;
                    g.DrawBezier(smile, -2.5f, 6.5f, -1.1f, lift, 1.2f, lift, 2.6f, 6.4f);
                    g.DrawBezier(feature, -0.6f, lift - 0.15f, -0.2f, lift + 0.35f, 0.3f, lift + 0.35f, 0.7f, lift - 0.15f);
                }
            }
        }

        private static GraphicsPath MakeAlmond(float centerX, float centerY, float height)
        {
            GraphicsPath eye = new GraphicsPath();
            float halfWidth = 2.65f;
            eye.AddBezier(centerX - halfWidth, centerY,
                          centerX - 1.3f, centerY - height,
                          centerX + 1.3f, centerY - height,
                          centerX + halfWidth, centerY);
            eye.AddBezier(centerX + halfWidth, centerY,
                          centerX + 1.3f, centerY + height * 0.72f,
                          centerX - 1.3f, centerY + height * 0.72f,
                          centerX - halfWidth, centerY);
            eye.CloseFigure();
            return eye;
        }

        private static void DrawHorizontalChilli(Graphics g, float y, float scale, bool flip, bool red, float rotation)
        {
            GraphicsState state = g.Save();
            g.TranslateTransform(0f, y);
            g.RotateTransform(rotation);
            g.ScaleTransform(flip ? -scale : scale, scale);

            Color light = red ? Color.FromArgb(255, 238, 62, 48) : Color.FromArgb(255, 72, 169, 74);
            Color dark = red ? Color.FromArgb(255, 151, 25, 29) : Color.FromArgb(255, 18, 99, 46);
            Color outline = red ? Color.FromArgb(235, 121, 18, 25) : Color.FromArgb(235, 12, 73, 36);

            using (GraphicsPath chilli = new GraphicsPath())
            {
                chilli.AddBezier(-20f, -4f, -10f, -7f, 7f, -2f, 16f, -5f);
                chilli.AddBezier(16f, -5f, 21f, -7f, 23f, -11f, 22f, -6f);
                chilli.AddBezier(22f, -6f, 21f, -1f, 17f, 2f, 12f, 3f);
                chilli.AddBezier(12f, 3f, 1f, 7f, -11f, 6f, -20f, 3f);
                chilli.AddBezier(-20f, 3f, -22f, 1f, -22f, -2f, -20f, -4f);
                chilli.CloseFigure();
                using (LinearGradientBrush fill = new LinearGradientBrush(new RectangleF(-23f, -10f, 47f, 18f), light, dark, 80f))
                using (Pen edge = new Pen(outline, 1.05f))
                using (Pen shine = new Pen(Color.FromArgb(105, 240, 255, 214), 1.15f))
                {
                    g.FillPath(fill, chilli);
                    g.DrawPath(edge, chilli);
                    g.DrawBezier(shine, -16f, -2f, -7f, -4f, 4f, 0f, 11f, -2f);
                }
            }

            using (Pen stem = new Pen(Color.FromArgb(255, 67, 100, 35), 1.8f))
            {
                stem.StartCap = stem.EndCap = LineCap.Round;
                g.DrawBezier(stem, -20f, -1f, -24f, -3f, -25f, -6f, -23f, -8f);
            }
            g.Restore(state);
        }

        private static void DrawBottomBead(Graphics g, float y)
        {
            using (SolidBrush shadow = new SolidBrush(Color.FromArgb(42, 0, 0, 0)))
            using (LinearGradientBrush bead = new LinearGradientBrush(new RectangleF(-4f, y - 3f, 8f, 10f),
                Color.FromArgb(255, 92, 62, 37), Color.FromArgb(255, 34, 25, 19), 75f))
            {
                g.FillEllipse(shadow, -4f, y - 2f, 9f, 10f);
                g.FillEllipse(bead, -4f, y - 3f, 8f, 10f);
            }
        }

        private void Present(Bitmap bitmap)
        {
            IntPtr screenDc = Native.GetDC(IntPtr.Zero);
            IntPtr memoryDc = Native.CreateCompatibleDC(screenDc);
            IntPtr hBitmap = bitmap.GetHbitmap(Color.FromArgb(0));
            IntPtr oldBitmap = Native.SelectObject(memoryDc, hBitmap);

            try
            {
                Native.SIZE size = new Native.SIZE(bitmap.Width, bitmap.Height);
                Native.POINT source = new Native.POINT(0, 0);
                Native.POINT destination = new Native.POINT(Left, Top);
                Native.BLENDFUNCTION blend = new Native.BLENDFUNCTION();
                blend.BlendOp = Native.AC_SRC_OVER;
                blend.SourceConstantAlpha = 255;
                blend.AlphaFormat = Native.AC_SRC_ALPHA;
                Native.UpdateLayeredWindow(Handle, screenDc, ref destination, ref size,
                    memoryDc, ref source, 0, ref blend, Native.ULW_ALPHA);
            }
            finally
            {
                Native.SelectObject(memoryDc, oldBitmap);
                Native.DeleteObject(hBitmap);
                Native.DeleteDC(memoryDc);
                Native.ReleaseDC(IntPtr.Zero, screenDc);
            }
        }
    }

    internal static class Native
    {
        internal const int WS_EX_LAYERED = 0x00080000;
        internal const int WS_EX_TOOLWINDOW = 0x00000080;
        internal const int WS_EX_NOACTIVATE = 0x08000000;
        internal const int ULW_ALPHA = 0x00000002;
        internal const byte AC_SRC_OVER = 0x00;
        internal const byte AC_SRC_ALPHA = 0x01;
        internal const int WM_NCHITTEST = 0x0084;
        internal const int HTTRANSPARENT = -1;
        internal const int HTCLIENT = 1;

        [StructLayout(LayoutKind.Sequential)]
        internal struct POINT
        {
            internal int X;
            internal int Y;
            internal POINT(int x, int y) { X = x; Y = y; }
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct SIZE
        {
            internal int Width;
            internal int Height;
            internal SIZE(int width, int height) { Width = width; Height = height; }
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        internal struct BLENDFUNCTION
        {
            internal byte BlendOp;
            internal byte BlendFlags;
            internal byte SourceConstantAlpha;
            internal byte AlphaFormat;
        }

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst,
            ref SIZE psize, IntPtr hdcSrc, ref POINT pptSrc, int crKey,
            ref BLENDFUNCTION pblend, int dwFlags);

        [DllImport("user32.dll")]
        internal static extern IntPtr GetDC(IntPtr hwnd);

        [DllImport("user32.dll")]
        internal static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);

        [DllImport("gdi32.dll")]
        internal static extern IntPtr CreateCompatibleDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        internal static extern bool DeleteDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        internal static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

        [DllImport("gdi32.dll")]
        internal static extern bool DeleteObject(IntPtr hObject);

        [DllImport("user32.dll")]
        internal static extern bool SetProcessDPIAware();
    }
}
