using System.Runtime.InteropServices;

namespace SurvivalEngine
{
    public class WebGLTool
    {

        public static bool isMobile()
        {
            return UnityEngine.Device.Application.isMobilePlatform;
        }

    }

}