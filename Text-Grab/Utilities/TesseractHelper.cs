﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Text_Grab.Models;
using CliWrap;
using System.Text;
using CliWrap.Buffered;
using Text_Grab.Interfaces;
using Text_Grab.Properties;

namespace Text_Grab.Utilities;

// Install Tesseract for Windows from UB-Mannheim
// https://github.com/UB-Mannheim/tesseract/wiki

// Docs about commandline usage
// https://tesseract-ocr.github.io/tessdoc/Command-Line-Usage.html 

// This was developed using Tesseract v5 in 2022

public static class TesseractHelper
{
    private const string rawPath = @"%LOCALAPPDATA%\Tesseract-OCR\tesseract.exe";
    private const string rawProgramsPath = @"%LOCALAPPDATA%\Programs\Tesseract-OCR\tesseract.exe";
    private const string basicPath = @"C:\Program Files\Tesseract-OCR\tesseract.exe";

    public static bool CanLocateTesseractExe()
    {
        string tesseractPath = string.Empty;
        try
        {
            tesseractPath = GetTesseractPath();
        }
        catch (Exception)
        {
            tesseractPath = string.Empty;
#if DEBUG
            throw;
#endif
        }
        return !string.IsNullOrEmpty(tesseractPath);
    }

    private static string GetTesseractPath()
    {
        if (!string.IsNullOrWhiteSpace(Settings.Default.TesseractPath)
            && File.Exists(Settings.Default.TesseractPath))
            return Settings.Default.TesseractPath;

        string tesExePath = Environment.ExpandEnvironmentVariables(rawPath);
        string programsPath = Environment.ExpandEnvironmentVariables(rawProgramsPath);

        if (File.Exists(tesExePath))
        {
            Settings.Default.TesseractPath = tesExePath;
            Settings.Default.Save();
            return tesExePath;
        }

        if (File.Exists(programsPath))
        {
            Settings.Default.TesseractPath = programsPath;
            Settings.Default.Save();
            return programsPath;
        }

        if (File.Exists(basicPath))
        {
            Settings.Default.TesseractPath = basicPath;
            Settings.Default.Save();
            return basicPath;
        }

        return string.Empty;
    }

    public static async Task<string> GetTextFromImagePathAsync(string imagePath, Windows.Globalization.Language language, string tessTag)
    {
        string tesseractPath = GetTesseractPath();

        if (string.IsNullOrWhiteSpace(tesseractPath))
            return "Cannot find tesseract.exe";

        // probably not needed, but if the Windows languages get passed it, it should still work
        string languageString = tessTag;

        BufferedCommandResult result = await Cli.Wrap(tesseractPath)
            .WithValidation(CommandResultValidation.None)
            .WithArguments(args => args
                .Add(imagePath)
                .Add("-")
                .Add("-l")
                .Add(languageString)
            )
            .ExecuteBufferedAsync();

        return result.StandardOutput;
    }

    public static async Task<OcrOutput> GetOcrOutputFromBitmap(Bitmap bmp, Windows.Globalization.Language language, string tessTag = "")
    {
        bmp.Save(TesseractHelper.TempImagePath(), ImageFormat.Png);

        OcrOutput ocrOutput = new OcrOutput()
        {
            Engine = OcrEngineKind.Tesseract,
            Kind = OcrOutputKind.Paragraph,
            SourceBitmap = bmp,
            RawOutput = await TesseractHelper.GetTextFromImagePathAsync(TempImagePath(), language, tessTag)
        };
        ocrOutput.CleanOutput();

        return ocrOutput;
    }

    public static async Task<string> GetTextFromImagePath(string pathToFile, bool outputHocr)
    {
        string tesExePath = GetTesseractPath();

        if (string.IsNullOrEmpty(tesExePath))
            return "Cannot find tesseract.exe";

        string argumentsString = $"\"{pathToFile}\" - -l eng";

        if (outputHocr)
            argumentsString += " hocr";

        ProcessStartInfo psi = new()
        {
            FileName = tesExePath,
            Arguments = argumentsString,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
        };

        Process? process = Process.Start(psi);

        if (process is null)
            return string.Empty;

        StreamReader sr = process.StandardOutput;
        StreamReader errorReader = process.StandardError;

        process.WaitForExit(1000);

        if (process.HasExited)
        {
            string returningResult = await sr.ReadToEndAsync();

            if (!string.IsNullOrWhiteSpace(returningResult))
                return returningResult;

            returningResult = await errorReader.ReadToEndAsync();

            return returningResult;
        }
        else
            return string.Empty;
    }

    public static string TempImagePath()
    {
        string? exePath = Path.GetDirectoryName(System.AppContext.BaseDirectory);
        if (exePath is null)
        {
            string rawPath = @"%LOCALAPPDATA%\Text_Grab";
            exePath = Environment.ExpandEnvironmentVariables(rawPath);
        }

        return $"{exePath}\\tempImage.png";
    }

    public async static Task<List<string>> TesseractLangsAsStrings()
    {
        List<string> languageStrings = new();

        string tesseractPath = GetTesseractPath();

        if (string.IsNullOrWhiteSpace(tesseractPath))
        {
            languageStrings.Add("eng");
            return languageStrings;
        }

        BufferedCommandResult result = await Cli.Wrap(tesseractPath)
            .WithValidation(CommandResultValidation.None)
            .WithArguments(args => args
                .Add("--list-langs")
            ).ExecuteBufferedAsync();

        if (string.IsNullOrWhiteSpace(result.StandardOutput))
        {
            languageStrings.Add("eng");
            return languageStrings;
        }

        string[] tempList = result.StandardOutput.Split(Environment.NewLine);

        foreach (string item in tempList)
            if (item.Length < 30 && !string.IsNullOrWhiteSpace(item) && item != "osd")
                languageStrings.Add(item);

        return languageStrings;
    }

    public async static Task<List<ILanguage>> TesseractLanguages()
    {
        List<string> languageStrings = await TesseractLangsAsStrings();
        List<ILanguage> tesseractLanguages = new();

        foreach (string language in languageStrings)
            tesseractLanguages.Add(new TessLang(language));

        return tesseractLanguages;
    }
}

public class TessLang : ILanguage
{
    private string _tessLangTag;

    public string AbbreviatedName => _tessLangTag;

    public string CurrentInputMethodLanguageTag => string.Empty;

    public string DisplayName => $"{_tessLangTag} with Tesseract";

    public Windows.Globalization.LanguageLayoutDirection LayoutDirection
    {
        get
        {
            if (_tessLangTag.Contains("vert"))
                return Windows.Globalization.LanguageLayoutDirection.TtbRtl;

            return Windows.Globalization.LanguageLayoutDirection.Rtl;
        }
    }

    public string NativeName => string.Empty;

    public string Script => string.Empty;

    public string LanguageTag => _tessLangTag;

    public TessLang(string tessLangTag)
    {
        _tessLangTag = tessLangTag;
    }
}

public class TessOcrLine
{
    public int Height { get; set; }
    public string Text { get; set; } = string.Empty;
    public int Width { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
}

public static class HocrReader
{
    public static List<TessOcrLine> ReadLines(string hocrText)
    {
        // Create a list to hold the OcrLine objects
        var lines = new List<TessOcrLine>();

        // Split the hOCR text into lines
        var hocrLines = hocrText.Split(new string[] { "<span class='ocr_line'", "</span>" }, StringSplitOptions.RemoveEmptyEntries);

        // Iterate through the lines
        foreach (var hocrLineText in hocrLines)
        {
            // Extract the line information
            var line = ReadLine(hocrLineText);

            // Add the line to the list
            lines.Add(line);
        }

        return lines;
    }

    private static TessOcrLine ReadLine(string hocrLineText)
    {
        // Create a new OcrLine object
        TessOcrLine line = new();

        // Extract the text of the line from the hOCR text
        var textMatch = Regex.Match(hocrLineText, "<span class='ocr_line'[^>]*>(.*?)</span>");
        line.Text = textMatch.Groups[1].Value;

        // Extract the bounding box coordinates from the hOCR text
        var bboxMatch = Regex.Match(hocrLineText, "bbox (\\d+) (\\d+) (\\d+) (\\d+)");
        line.X = int.Parse(bboxMatch.Groups[1].Value);
        line.Y = int.Parse(bboxMatch.Groups[2].Value);
        line.Width = int.Parse(bboxMatch.Groups[3].Value);
        line.Height = int.Parse(bboxMatch.Groups[4].Value);

        return line;
    }
}