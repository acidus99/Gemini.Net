using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;

using Gemini.Net.Utils;

namespace Gemini.Net;

/// <summary>
/// 
/// </summary>
public static class GeminiParser
{

    /// <summary>
    /// We use status code 49 for any connection-related errors. (e.g. DNS failure, no port listening, TLS failure, timeout failure, etc)
    /// The exact error message will appear in the meta line.
    /// This ensures all Gemini responses have a status code
    /// </summary>
    public const int ConnectionErrorStatusCode = 49;

    public static bool IsInputStatus(int statusCode) => InStatusRange(statusCode, 10);
    public static bool IsSuccessStatus(int statusCode) => InStatusRange(statusCode, 20);
    public static bool IsRedirectStatus(int statusCode) => InStatusRange(statusCode, 30);
    public static bool IsTempFailStatus(int statusCode) => InStatusRange(statusCode, 40);
    public static bool IsPermFailStatus(int statusCode) => InStatusRange(statusCode, 50);
    public static bool IsAuthStatus(int statusCode) => InStatusRange(statusCode, 60);

    private static bool InStatusRange(int statusCode, int lowRange)
        => (statusCode >= lowRange && statusCode <= lowRange + 9);

    /// <summary>
    /// Standalong gemini response parser, given a byte array
    /// </summary>
    /// <param name="url"></param>
    /// <param name="response"></param>
    /// <returns></returns>
    /// <exception cref="ArgumentNullException"></exception>
    /// <exception cref="ArgumentException"></exception>
    public static GeminiResponse ParseBytes(GeminiUrl url,  byte [] response)
    {
        if(url == null)
        {
            throw new ArgumentNullException(nameof(url));
        }

        if(response == null)
        {
            throw new ArgumentNullException(nameof(response));
        }

        if(response.Length < 5)
        {
            throw new ArgumentException(@"Malformed Gemini response. Response line too short.", nameof(response));
        }

        int endIndex = FindEndResponseLine(response);
        if(endIndex == -1)
        {
            throw new ArgumentException(@"Malformed Gemini response. Missing response line ending.n", nameof(response));
        }
        if(endIndex < 3)
        {
            throw new ArgumentException(@"Malformed Gemini response. Response line ending appears too early.", nameof(response));
        }

        var responseLine = Encoding.UTF8.GetString(response, 0, endIndex);

        responseLine = CleanLegacyResponseLne(responseLine);

        //response line parsed by constructor. Throws an exception on invalid formats
        GeminiResponse ret = new GeminiResponse(url, responseLine);
        if (response.Length > endIndex + 2)
        {
            //the rest of the byte array is the body
            ret.ParseBody(response.Skip(endIndex + 2).ToArray());
        }

        return ret;
    }

    public static byte[] RequestBytes(GeminiUrl url)
        => Encoding.UTF8.GetBytes($"{url}\r\n");

    /// <summary>
    /// Early Gemini systems that used a tab between the status and the META. Clean that
    /// </summary>
    /// <param name="line"></param>
    /// <returns></returns>
    private static string CleanLegacyResponseLne(string line)
        => line.Replace('\t', ' ');

    /// <summary>
    /// finds the index of the \r in the \r\n of the delimiter between the end of the response line
    /// and the (optional) body
    /// </summary>
    /// <param name="response"></param>
    /// <returns>-1 means it wasn't found, indicating an invalid response</returns>
    private static int FindEndResponseLine(byte[] response)
    {
        for (int i = 0; i < response.Length - 1; i++)
        {
            if (response[i] == (byte)'\r' &&
                response[i + 1] == (byte)'\n')
            {
                return i;
            }
        }
        return -1;
    }
}

