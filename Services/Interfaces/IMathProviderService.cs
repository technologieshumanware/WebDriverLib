// WebDriverLib
// This file is covered by the GNU General Public License.
// Copyright (C) 2026 Technologies Humanware

namespace WebDriverLib.Services.Interfaces
{
    public interface IMathProviderService
    {
        /// <summary>
        /// Transform a MathML snippet to a braille string, depening on the current braille math code used.
        /// </summary>
        /// <param name="mathML">The MathML snippet to transform.</param>
        /// <returns></returns>
        public string GetBrailleFromMathML(string mathML);

        /// <summary>
        /// Transform a MathML snippet to its natural language representation.
        /// </summary>
        /// <param name="mathML">The MathML snippet to transform.</param>
        /// <returns></returns>
        public string GetSpeechForMathML(string mathML);

        /// <summary>
        /// Close the chrome driver object used for HTML manipulations.
        /// </summary>
        public void CloseAndDisposeChromeDriver();

        /// <summary>
        /// Transform a latex string to a MathML string.
        /// </summary>
        /// <param name="latex">The latex string to transform.</param>
        /// <returns></returns>
        public string GetMathMLFromLatex(string latex);

        /// <summary>
        /// Transform a latex string to its natural language representation.
        /// </summary>
        /// <param name="latex">The latex string to transform.</param>
        /// <returns></returns>
        public string GetSpeechFromLatex(string latex);
    }
}
