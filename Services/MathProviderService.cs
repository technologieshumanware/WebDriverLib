// WebDriverLib
// This file is covered by the GNU General Public License.
// Copyright (C) 2026 Technologies Humanware

using Humanware.NLogger;
using Microsoft.Win32;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using WebDriverLib.Constants;
using WebDriverLib.Services.Interfaces;

namespace WebDriverLib.Services
{
    public class MathProviderService : IMathProviderService
    {
        private ChromeDriver? _chromeDriver;

        private const string _mathCodeRegistryKey = @"SOFTWARE\Humanware\Settings";
        private const string _mathCodeValueName = "MathBrailleCode";

        public MathProviderService()
        {
            //The TTL configuration affects how long the metadata file (se-metadata.json)
            //in the cache is considered valid before Selenium Manager makes network requests
            //to verify browser and driver versions again. 

            //Default is 1hour. By setting a longer TTL (2 days) we allow for a work session that'd still works if device
            //doesn't have network connection. 
            //without having to go to Offline mode (which would cause problem whenever browser versions change)
            Environment.SetEnvironmentVariable("SE_TTL", "172800");
        
            _ = Task.Run(SetupChromeDriver);
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

            var mathml2brailleSnippet = GetResourcePath(Path.Combine("Mathml2braille", "MathSnippet.xhtml"));
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

            var mathJaxSnippet = GetResourcePath(Path.Combine("MathJax", "MathJaxSnippet.xhtml"));
            var language = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;

            UpdateMathJaxSnippetContentWithLanguage(mathJaxSnippet, language);

            _chromeDriver.Navigate().GoToUrl(mathJaxSnippet);

            _chromeDriver.ExecuteScript(@"
                var mml = document.getElementById('MathJaxInput');
                mml.innerHTML = arguments[0];",
                htmlContent);

            return true;
        }

        private void SetupChromeDriver()
        {
            var chromeService = ChromeDriverService.CreateDefaultService();
            chromeService.HideCommandPromptWindow = true;

            var chromeOptions = new ChromeOptions();
            chromeOptions.AddArgument("headless");
            chromeOptions.AddArgument("allow-file-access-from-files");

            _chromeDriver = new ChromeDriver(chromeService, chromeOptions);
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

        private static void UpdateMathJaxSnippetContentWithLanguage(string path, string language)
        {
            var htmlContent = File.ReadAllText(path);

            var match = Regex.Match(htmlContent, @"locale:\s*'([^']*)'");
            if (match.Success)
            {
                string previousLanguage = match.Groups[1].Value;
                if (string.Equals(path, previousLanguage, StringComparison.OrdinalIgnoreCase))
                {
                    NLogger.Debug($"Already the right language: {language}");
                    return;
                }

                NLogger.Debug($"Replacing locale language: {language}");
                var replacement = Regex.Replace(htmlContent, $"'{previousLanguage}'", $"'{language}'");
                File.WriteAllText(path, replacement);
            }
        }

        private static string FormatMathML(string mathML)
        {
            return mathML
                .Replace(@"<mml:math xmlns:mml=""http://www.w3.org/1998/Math/MathML"" display=""block"">", @"<math xmlns=""http://www.w3.org/1998/Math/MathML"" display=""block"">")
                .Replace("<mml:", "<")
                .Replace("</mml:", "</");
        }

        private static string GetMathCodeFromRegistry()
        {
            using var registryKey = Registry.CurrentUser.OpenSubKey(_mathCodeRegistryKey);
            if (registryKey is null)
            {
                return MathCodeConstants.NemethMathCode;
            }

            var mathCode = registryKey.GetValue(_mathCodeValueName)?.ToString();
            return mathCode ?? MathCodeConstants.NemethMathCode;
        }
    }
}
