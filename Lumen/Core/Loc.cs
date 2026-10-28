using Windows.ApplicationModel.Resources;

namespace Lumen.Core
{
    /// <summary>Runtime access to Strings\*\Resources.resw.</summary>
    public static class Loc
    {
        private static ResourceLoader _loader;

        public static string Get(string key)
        {
            try
            {
                if (_loader == null) _loader = ResourceLoader.GetForViewIndependentUse();
                var s = _loader.GetString(key);
                return string.IsNullOrEmpty(s) ? key : s;
            }
            catch
            {
                return key;
            }
        }

        public static string Format(string key, params object[] args)
        {
            return string.Format(Get(key), args);
        }
    }
}
