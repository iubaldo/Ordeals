using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.ServerMods;
using Vintagestory.GameContent;

using Cairo;

namespace ordeals.src
{
    class OrdealSplash : HudElement
    {
        private int tickInterval = 20;
        private long durationActiveMs = 0;
        private int durationVisibleMs = 6000;
        private Vec4f fadeColor = new Vec4f(1f, 1f, 1f, 1f);

        public GuiElementImage element;
        public BitmapRef splashImage;

        private ElementBounds bounds = new ElementBounds
        {
            Alignment = EnumDialogArea.CenterMiddle,
            BothSizing = ElementSizing.Percentual,
            percentWidth = 1.0,
            percentHeight = 1.0
        };

        public bool isActive = false;

        public override EnumDialogType DialogType { get { return EnumDialogType.HUD; } }
        public override double InputOrder { get { return 1.0; } }
        public override string ToggleKeyCombinationCode { get { return null; } }


        public OrdealSplash(ICoreClientAPI capi) : base(capi)
        {
            capi.Event.RegisterGameTickListener(new Action<float>(onGameTick), tickInterval);
        }


        private void onGameTick(float dt)
        {
            if (!isActive && durationActiveMs == 0L)
                return;

            if (durationActiveMs == 0L)
            {
                durationActiveMs = capi.InWorldEllapsedMilliseconds;
                fadeColor.A = 0f;
                TryOpen();
            }

            long visibleMsPassed = capi.InWorldEllapsedMilliseconds - durationActiveMs;
            long visibleMsLeft = durationVisibleMs - visibleMsPassed;

            if (visibleMsLeft < 0L)
            {
                durationActiveMs = 0L;
                TryClose();
            }

            if (visibleMsLeft < 250L)
                fadeColor.A = visibleMsPassed / 240L;
            else
                fadeColor.A = 1L;

            if (visibleMsLeft < 1000L)
                fadeColor.A = visibleMsLeft / 990L;
        }


        public void setSplashImage(AssetLocation splashLoc)
        {
            // element = new GuiElementImage(capi, bounds, splashLoc);
            splashImage = capi.Assets.Get(splashLoc).ToBitmap(capi);
           
            SingleComposer = capi.Gui.CreateCompo("ordealSplashComposer", bounds)
                .AddStaticCustomDraw(bounds, new DrawDelegateWithBounds(onDraw))
                .Compose();
        }
   

        private void onDraw(Context context, ImageSurface surface, ElementBounds currentBounds)
        {
            // surface.Image(((BitmapExternal) splashImage).bmp, 0, 0, 960, 540);
            surface.Image(((BitmapExternal)splashImage).bmp, (int)currentBounds.drawX, (int)currentBounds.drawY, (int)currentBounds.InnerWidth, (int)currentBounds.InnerHeight);          
        }
    }
}
