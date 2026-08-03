using System;
using LibreSpotUWP.Services;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;

namespace LibreSpotUWP.Helpers
{
    public static class XboxExperience
    {
        public const bool UseMouseMode = true;

        private static bool _applied;

        public static void Apply()
        {
            if (_applied) { return; }
            _applied = true;

            if (!OSHelper.IsXboxFamily) { return; }

            ApplyPointerMode();
            ApplyFullBleed();
        }

        private static void ApplyPointerMode()
        {
            try
            {
                Application.Current.RequiresPointerMode = UseMouseMode
                    ? ApplicationRequiresPointerMode.Auto
                    : ApplicationRequiresPointerMode.WhenRequested;
            }
            catch (Exception ex)
            {
                LogService.Warn("XboxExperience: pointer mode not applied - " + ex.Message);
            }
        }

        private static void ApplyFullBleed()
        {
            try
            {
                ApplicationView.GetForCurrentView()
                    .SetDesiredBoundsMode(ApplicationViewBoundsMode.UseCoreWindow);
            }
            catch (Exception ex)
            {
                LogService.Warn("XboxExperience: bounds mode not applied - " + ex.Message);
            }
        }
    }
}
