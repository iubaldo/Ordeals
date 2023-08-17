using System;

using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

using Cairo;

namespace Ordeals.src
{
    class OrdealSplash : HudElement
    {
        private int tickInterval = 20;
        private long durationActiveMs = 0;
        private int durationVisibleMs = 8000;
        private Vec4f fadeColor = new Vec4f(1f, 1f, 1f, 0f);

        public BitmapRef splashImage;

        public bool isActive = false;

        public override EnumDialogType DialogType { get { return EnumDialogType.HUD; } }
        public override double InputOrder { get { return 1.0; } }
        public override string ToggleKeyCombinationCode { get { return null; } }


        public OrdealSplash(ICoreClientAPI capi) : base(capi)
        {
            capi.Event.RegisterGameTickListener(new Action<float>(OnGameTick), tickInterval);
        }


        private void OnGameTick(float dt)
        {
            if (splashImage == null)
                return;

            if (!isActive && durationActiveMs == 0L)
                return;

            if (durationActiveMs == 0L) // start animating
            {
                durationActiveMs = capi.InWorldEllapsedMilliseconds;
                fadeColor.A = 0f;
                TryOpen();
                return;
            }

            long visibleMsPassed = capi.InWorldEllapsedMilliseconds - durationActiveMs;
            long visibleMsLeft = durationVisibleMs - visibleMsPassed;

            if (visibleMsLeft <= 0L) // stop animating
            {
                isActive = false;
                durationActiveMs = 0L;
                TryClose();
                
                return;
            }

            if (visibleMsPassed < 2000L) // fade in
                fadeColor.A = visibleMsPassed / 1990f;
            else
                fadeColor.A = 1f;

            if (visibleMsLeft < 2000L) // fade out
                fadeColor.A = visibleMsLeft / 1990f;          

            // System.Diagnostics.Debug.WriteLine("set fade to (" + fadeColor.R + ", " + fadeColor.G + ", " + fadeColor.B + ", " + fadeColor.A + ")");
            SingleComposer.Color = fadeColor;
        }


        public void SetSplashImage(AssetLocation splashLoc)
        {
            splashImage = capi.Assets.Get(splashLoc).ToBitmap(capi);
        }
   

        private void OnDraw(Context context, ImageSurface surface, ElementBounds currentBounds)
        {
            surface.Image(((BitmapExternal)splashImage).bmp, (int)currentBounds.drawX, (int)currentBounds.drawY, (int)currentBounds.InnerWidth, (int)currentBounds.InnerHeight);
        }


        public override bool TryOpen()
        {
            ElementBounds bounds = CalculateBounds();
            SingleComposer = capi.Gui.CreateCompo("ordealSplashComposer", bounds)
                .AddStaticCustomDraw(bounds, new DrawDelegateWithBounds(OnDraw))
                .Compose();
            SingleComposer.Color = fadeColor;

            return base.TryOpen();
        }


        private ElementBounds CalculateBounds()
        {
            return new ElementBounds
            {
                Alignment = EnumDialogArea.CenterMiddle,
                BothSizing = ElementSizing.Percentual,
                percentWidth = 1.0,
                percentHeight = 1.0
            };
        }
    }
}
