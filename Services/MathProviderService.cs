// WebDriverLib
// This file is covered by the GNU General Public License.
// Copyright (C) 2026 Technologies Humanware

using Humanware.NLogger;
using Microsoft.Win32;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.RegularExpressions;
using WebDriverLib.Constants;
using WebDriverLib.Services.Interfaces;

namespace WebDriverLib.Services
{
    public class MathProviderService : IMathProviderService
    {
        private ChromeDriver? _chromeDriver;

        private const string MathCodeRegistryKey = @"SOFTWARE\Humanware\Settings";
        private const string MathCodeValueName = "MathBrailleCode";
        private const string Mathml2Braille = "Mathml2braille";
        private const string MathMl2BrailleSnippet = "MathSnippet.xhtml";
        private const string MathJax = "MathJax";
        private const string MathJaxSnippet = "MathJaxSnippet.xhtml";
        private const string PinnedChromeVersion = ""; // Chrome version
        private const string ChromeDriverDownloadUrl = ""; // URI to chromedriver-win64.zip
        private const string ChromeDownloadUrl = ""; // URI to chrome-win64
        private static readonly string SeleniumCacheRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache", "selenium");
        private static readonly string ChromeDriverPath = Path.Combine(SeleniumCacheRoot, "chromedriver", "win64", PinnedChromeVersion, "chromedriver.exe");
        private static readonly string ChromeBinaryPath = Path.Combine(SeleniumCacheRoot, "chrome", "win64", PinnedChromeVersion, "chrome.exe");

        public MathProviderService()
        {
            // We manage Chrome and ChromeDriver ourselves at a pinned version,
            // so Selenium Manager is not needed and should not make any network calls.
            Environment.SetEnvironmentVariable("SE_OFFLINE", "true");

            _ = Task.Run(async () =>
                {
                    await EnsurePinnedChromeAndDriverInstalled();
                    SetupChromeDriver();
                })
                .ContinueWith(_ => ClearSeleniumCache());
        }

        public void CloseAndDisposeChromeDriver() => _chromeDriver?.Quit();

        public string GetBrailleFromMathML(string mathML)
        {
            try
            {
                return GetBrailleFromMathMLInternal(mathML);
            }
            catch (WebDriverException e)  //we can get expired session id if timeout, if sleep, etc... (BNW-2703)
            {
                NLogger.Warn("GetBrailleFromMathML WebDriverException " + e.Message);
                SetupChromeDriver();
                return GetBrailleFromMathMLInternal(mathML);
            }
        }

        private string GetBrailleFromMathMLInternal(string mathML)
        {
            if (_chromeDriver is null)
            {
                NLogger.Warn("Chrome driver is null");
                return string.Empty;
            }

            var mathml2brailleSnippet = GetResourcePath(Path.Combine(Mathml2Braille, MathMl2BrailleSnippet));
            _chromeDriver.Navigate().GoToUrl(mathml2brailleSnippet);

            var newMml = FormatMathML(mathML);
            var mathCode = GetMathCodeFromRegistry().ToLower();

            _chromeDriver.ExecuteScript(@"
                var mml = document.getElementById('MathMLPlaceHolder');
                mml.innerHTML = arguments[0];
                var options1 = { codeBrailleMath: arguments[1], maxCaracCell: 14 };
                new Mathml2braille(options1);",
                newMml, mathCode);

            var braille = _chromeDriver.ExecuteScript("return getExpressionInBraille();");
            return braille as string ?? string.Empty;
        }

        public string GetSpeechForMathML(string mathML)
        {
            var newMml = FormatMathML(mathML);
            try
            {
                return GetSpeechForMathMLInternal(newMml);
            }
            catch (WebDriverException e)
            {
                NLogger.Warn("GetSpeechForMathML WebDriverException " + e.Message);
                SetupChromeDriver();
                return GetSpeechForMathML(newMml);
            }
        }

        private string GetSpeechForMathMLInternal(string newMml)
        {
            if (!SetupMathJax(newMml) || _chromeDriver is null)
            {
                return string.Empty;
            }

            var result = _chromeDriver.ExecuteScript(@"
                MathJax.typeset();
                return getTTSText();");

            return result as string ?? string.Empty;
        }

        public string GetMathMLFromLatex(string latex) 
        {
            try
            {
                return GetMathMLFromLatexInternal(latex);
            }
            catch (WebDriverException e)
            {
                NLogger.Warn("GetMathMLFromLatex WebDriverException " + e.Message);
                SetupChromeDriver();
                return GetMathMLFromLatex(latex);
            }
        }

        private string GetMathMLFromLatexInternal(string latex)
        {
            // Wrap latex string as inline equation and escape less-than signs for HTML
            latex = $"\\({latex.Replace("<", "&lt;")}\\)";

            if (!SetupMathJax(latex) || _chromeDriver is null)
            {
                return string.Empty;
            }

            var result = _chromeDriver.ExecuteScript(@"
                MathJax.typeset();
                return getConvertedTex();");

            return result as string ?? string.Empty;
        }

        public string GetSpeechFromLatex(string latex)
        {
            try
            {
                return GetSpeechFromLatexInternal(latex);
            }
            catch (WebDriverException e)
            {
                NLogger.Warn("GetSpeechFromLatex WebDriverException " + e.Message);
                SetupChromeDriver();
                return GetSpeechFromLatex(latex);
            }
        }

        private string GetSpeechFromLatexInternal(string latex)
        {
            // Wrap latex string as inline equation and escape less-than signs for HTML
            latex = $"\\({latex.Replace("<", "&lt;")}\\)";

            if (!SetupMathJax(latex) || _chromeDriver is null)
            {
                return string.Empty;
            }

            var result = _chromeDriver.ExecuteScript(@"
                MathJax.typeset();
                return getTTSText();");

            return result as string ?? string.Empty;
        }

        private bool SetupMathJax(string htmlContent)
        {
            if (_chromeDriver is null)
            {
                NLogger.Warn("Chrome driver is null");
                return false;
            }

            try
            {
                if (!_chromeDriver.Url.EndsWith(MathJaxSnippet))
                {
                    var mathJaxSnippet = GetResourcePath(Path.Combine(MathJax, MathJaxSnippet));
                    _chromeDriver.Navigate().GoToUrl(mathJaxSnippet);
                }

                _chromeDriver.ExecuteScript(@"
                    var mml = document.getElementById('MathJaxInput');
                    mml.innerHTML = arguments[0];",
                    htmlContent);

                return true;
            }
            catch (Exception ex)
            {
                NLogger.Error("Failed to setup MathJax snippet", ex);
                return false;
            }
        }

        private void SetupChromeDriver()
        {
            var chromeService = ChromeDriverService.CreateDefaultService(ChromeDriverPath);
            chromeService.HideCommandPromptWindow = true;

            var language = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;

            var chromeOptions = new ChromeOptions();
            chromeOptions.BinaryLocation = ChromeBinaryPath;
            chromeOptions.AddArgument("headless");
            chromeOptions.AddArgument("allow-file-access-from-files");
            chromeOptions.AddArgument($"lang={language}");

            _chromeDriver = new ChromeDriver(chromeService, chromeOptions);
        }

        /// <summary>
        /// Ensures Chrome and ChromeDriver at <see cref="PinnedChromeVersion"/> are present in the Selenium cache.
        /// Downloads and extracts them from the Humanware S3 bucket if missing.
        /// </summary>
        private async Task EnsurePinnedChromeAndDriverInstalled()
        {
            if (!File.Exists(ChromeDriverPath))
            {
                NLogger.Info($"Downloading ChromeDriver {PinnedChromeVersion}...");
                await DownloadAndExtractZip(
                        ChromeDriverDownloadUrl,
                        Path.GetDirectoryName(ChromeDriverPath)!,
                        "chromedriver-win64");
            }

            if (!File.Exists(ChromeBinaryPath))
            {
                NLogger.Info($"Downloading Chrome {PinnedChromeVersion}...");
                await DownloadAndExtractZip(
                        ChromeDownloadUrl,
                        Path.GetDirectoryName(ChromeBinaryPath)!,
                        "chrome-win64");
            }
        }

        /// <summary>
        /// Downloads a zip from target URL, extracts the contents of
        /// the inner zip folder to the target directory.
        /// </summary>
        /// <param name="url">The url from which to download content</param>
        /// <param name="targetDirectory">The directory created to extract the contents</param>
        /// <param name="innerZipFolder">The inner zip folder to extract</param>

        private async Task DownloadAndExtractZip(string url, string targetDirectory, string innerZipFolder)
        {
            var tempZip = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".zip");
            var tempExtractDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

            try
            {
#if NET48
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls13;
#endif
                using (var client = new HttpClient())
                {
                    var bytes = await client.GetByteArrayAsync(url);
                    File.WriteAllBytes(tempZip, bytes);
                }

                ZipFile.ExtractToDirectory(tempZip, tempExtractDir);

                var sourceDir = Path.Combine(tempExtractDir, innerZipFolder);
                Directory.CreateDirectory(targetDirectory);

                foreach (var file in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
                {
                    var relativePath = file.Substring(sourceDir.Length).TrimStart(Path.DirectorySeparatorChar);
                    var destFile = Path.Combine(targetDirectory, relativePath);
                    var destDir = Path.GetDirectoryName(destFile);
                    if (destDir != null)
                    {
                        Directory.CreateDirectory(destDir);
                    }
                    File.Copy(file, destFile, overwrite: true);
                }
            }
            catch (Exception ex)
            {
                NLogger.Error($"Failed to download and extract {url}", ex);
                throw;
            }
            finally
            {
                if (File.Exists(tempZip))
                {
                    File.Delete(tempZip);
                }
                if (Directory.Exists(tempExtractDir))
                {
                    Directory.Delete(tempExtractDir, recursive: true);
                }
            }
        }

        /// <summary>
        /// Clears old Chrome and ChromeDriver versions from the Selenium cache,
        /// keeping only <see cref="PinnedChromeVersion"/>.
        /// </summary>
        private static void ClearSeleniumCache()
        {
            DeleteOldCacheVersions(Path.Combine(SeleniumCacheRoot, "chromedriver", "win64"));
            DeleteOldCacheVersions(Path.Combine(SeleniumCacheRoot, "chrome", "win64"));
        }

        private static void DeleteOldCacheVersions(string cachePath)
        {
            var dirInfo = new DirectoryInfo(cachePath);
            if (!dirInfo.Exists)
            {
                return;
            }

            var directoriesToDelete = dirInfo.GetDirectories()
                .Where(x => x.Name != PinnedChromeVersion);

            foreach (var directory in directoriesToDelete)
            {
                try
                {
                    directory.Delete(recursive: true);
                }
                catch (Exception ex)
                {
                    NLogger.Error($"Failed to delete Selenium cache directory: {directory.FullName}", ex);
                }
            }
        }

        private static string GetResourcePath(string relativePath)
        {
            var assemblyPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            var filePath = Path.Combine(assemblyPath, "Resources", relativePath);

            if (!File.Exists(filePath))
            {
                // If resource not found in assembly location, get it from AppDomain location
                filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", relativePath);
            }

            return filePath;
        }

        private static readonly Dictionary<int, int> MathItalicToGreek = new Dictionary<int, int>
        {
            // -----------------------------
            // Uppercase Greek Italic (U+1D6E2–U+1D6FB)
            // Maps to Greek uppercase (U+0391–U+03A9)
            // -----------------------------
            { 0x1D6E2, 0x0391 }, // ?? ? ?  (Alpha)
            { 0x1D6E3, 0x0392 }, // ?? ? ?  (Beta)
            { 0x1D6E4, 0x0393 }, // ?? ? G  (Gamma)
            { 0x1D6E5, 0x0394 }, // ?? ? ?  (Delta)
            { 0x1D6E6, 0x0395 }, // ?? ? ?  (Epsilon)
            { 0x1D6E7, 0x0396 }, // ?? ? ?  (Zeta)
            { 0x1D6E8, 0x0397 }, // ?? ? ?  (Eta)
            { 0x1D6E9, 0x0398 }, // ?? ? T  (Theta)
            { 0x1D6EA, 0x0399 }, // ?? ? ?  (Iota)
            { 0x1D6EB, 0x039A }, // ?? ? ?  (Kappa)
            { 0x1D6EC, 0x039B }, // ?? ? ?  (Lambda)
            { 0x1D6ED, 0x039C }, // ?? ? ?  (Mu)
            { 0x1D6EE, 0x039D }, // ?? ? ?  (Nu)
            { 0x1D6EF, 0x039E }, // ?? ? ?  (Xi)
            { 0x1D6F0, 0x039F }, // ?? ? ?  (Omicron)
            { 0x1D6F1, 0x03A0 }, // ?? ? ?  (Pi)
            { 0x1D6F2, 0x03A1 }, // ?? ? ?  (Rho)
            { 0x1D6F3, 0x03F4 }, // ?? ? ?  (Theta symbol – special case)
            { 0x1D6F4, 0x03A3 }, // ?? ? S  (Sigma)
            { 0x1D6F5, 0x03A4 }, // ?? ? ?  (Tau)
            { 0x1D6F6, 0x03A5 }, // ?? ? ?  (Upsilon)
            { 0x1D6F7, 0x03A6 }, // ?? ? F  (Phi)
            { 0x1D6F8, 0x03A7 }, // ?? ? ?  (Chi)
            { 0x1D6F9, 0x03A8 }, // ?? ? ?  (Psi)
            { 0x1D6FA, 0x03A9 }, // ?? ? O  (Omega)
            { 0x1D6FB, 0x2207 }, // ?? ? ?  (Nabla – special case)

            // -----------------------------
            // Lowercase Greek Italic (U+1D6FC–U+1D715)
            // Maps to Greek lowercase (U+03B1–U+03C9)
            // -----------------------------
            { 0x1D6FC, 0x03B1 }, // ?? ? a  (alpha)
            { 0x1D6FD, 0x03B2 }, // ?? ? ß  (beta)
            { 0x1D6FE, 0x03B3 }, // ?? ? ?  (gamma)
            { 0x1D6FF, 0x03B4 }, // ?? ? d  (delta)
            { 0x1D700, 0x03B5 }, // ?? ? e  (epsilon)
            { 0x1D701, 0x03B6 }, // ?? ? ?  (zeta)
            { 0x1D702, 0x03B7 }, // ?? ? ?  (eta)
            { 0x1D703, 0x03B8 }, // ?? ? ?  (theta)
            { 0x1D704, 0x03B9 }, // ?? ? ?  (iota)
            { 0x1D705, 0x03BA }, // ?? ? ?  (kappa)
            { 0x1D706, 0x03BB }, // ?? ? ?  (lambda)
            { 0x1D707, 0x03BC }, // ?? ? µ  (mu)
            { 0x1D708, 0x03BD }, // ?? ? ?  (nu)
            { 0x1D709, 0x03BE }, // ?? ? ?  (xi)
            { 0x1D70A, 0x03BF }, // ?? ? ?  (omicron)
            { 0x1D70B, 0x03C0 }, // ?? ? p  (pi)
            { 0x1D70C, 0x03C1 }, // ?? ? ?  (rho)
            { 0x1D70D, 0x03C2 }, // ?? ? ?  (final sigma)
            { 0x1D70E, 0x03C3 }, // ?? ? s  (sigma)
            { 0x1D70F, 0x03C4 }, // ?? ? t  (tau)
            { 0x1D710, 0x03C5 }, // ?? ? ?  (upsilon)
            { 0x1D711, 0x03C6 }, // ?? ? f  (phi)
            { 0x1D712, 0x03C7 }, // ?? ? ?  (chi)
            { 0x1D713, 0x03C8 }, // ?? ? ?  (psi)
            { 0x1D714, 0x03C9 }, // ?? ? ?  (omega)
            { 0x1D715, 0x2202 }, // ?? ? ?  (partial derivative – special case)
        };

        /// <summary>
        /// Replace Math Alphanumeric Italic Greek character entities with Greek block equivalents.
        /// Handles numeric hex entities (e.g., &#x1D6FC;).
        /// Leaves unrelated characters untouched.
        /// </summary>
        private static string ReplaceMathItalicGreek(string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return input;
            }
            
            // Regex to match hex numeric entities like &#x1D6FC;
            Regex entityPattern = new Regex("&#x([0-9A-Fa-f]+);");

            return entityPattern.Replace(input, match =>
            {
                string hexValue = match.Groups[1].Value;

                try
                {
                    int codePoint = Convert.ToInt32(hexValue, 16);

                    if (MathItalicToGreek.TryGetValue(codePoint, out int greekCodePoint))
                    {
                        // Return as a Greek entity, not a literal character
                        return "&#x" + greekCodePoint.ToString("X") + ";";
                    }

                    // No mapping: leave original entity untouched
                    return match.Value;
                }
                catch
                {
                    // Invalid hex or overflow: leave as-is
                    return match.Value;
                }
            });
        }

        private static string FormatMathML(string mathML)
        {
            //normalize the Greek characters in there
            //because we're dealing with mathML that is already encoded, 
            //such characters appear as HTML entities such as &#x1D6FC for italic math alpha

            string normalized = ReplaceMathItalicGreek(mathML);


            return normalized
                .Replace(@"<mml:math xmlns:mml=""http://www.w3.org/1998/Math/MathML"" display=""block"">", @"<math xmlns=""http://www.w3.org/1998/Math/MathML"" display=""block"">")
                .Replace("<mml:", "<")
                .Replace("</mml:", "</");
        }

        private static string GetMathCodeFromRegistry()
        {
            using var registryKey = Registry.CurrentUser.OpenSubKey(MathCodeRegistryKey);
            if (registryKey is null)
            {
                return MathCodeConstants.NemethMathCode;
            }

            var mathCode = registryKey.GetValue(MathCodeValueName)?.ToString();
            return mathCode ?? MathCodeConstants.NemethMathCode;
        }
    }
}
