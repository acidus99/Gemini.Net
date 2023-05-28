using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
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

    public static bool IsInputStatus(int statusCode) => isStatusInRange(statusCode, 10);
    public static bool IsSuccessStatus(int statusCode) => isStatusInRange(statusCode, 20);
    public static bool IsRedirectStatus(int statusCode) => isStatusInRange(statusCode, 30);
    public static bool IsTempFailStatus(int statusCode) => isStatusInRange(statusCode, 40);
    public static bool IsPermFailStatus(int statusCode) => isStatusInRange(statusCode, 50);
    public static bool IsAuthStatus(int statusCode) => isStatusInRange(statusCode, 60);

    private static bool isStatusInRange(int statusCode, int lowRange)
        => (statusCode >= lowRange && statusCode <= lowRange + 9);

    /// <summary>
    /// Standalong gemini response parser, given a byte array
    /// </summary>
    /// <param name="url"></param>
    /// <param name="responseBytes"></param>
    /// <returns></returns>
    /// <exception cref="ArgumentNullException"></exception>
    /// <exception cref="ArgumentException"></exception>
    public static GeminiResponse ParseResponseBytes(GeminiUrl url,  byte [] responseBytes)
    {
        if(url == null)
        {
            throw new ArgumentNullException(nameof(url));
        }

        if(responseBytes == null)
        {
            throw new ArgumentNullException(nameof(responseBytes));
        }

        if(responseBytes.Length < 5)
        {
            throw new ArgumentException(@"Malformed Gemini response. Response line too short.", nameof(responseBytes));
        }

        int endIndex = FindEndResponseLine(responseBytes);
        if(endIndex == -1)
        {
            throw new ArgumentException(@"Malformed Gemini response. Missing response line ending.n", nameof(responseBytes));
        }
        if(endIndex < 3)
        {
            throw new ArgumentException(@"Malformed Gemini response. Response line ending appears too early.", nameof(responseBytes));
        }

        var responseLine = Encoding.UTF8.GetString(responseBytes, 0, endIndex);
        responseLine = CleanLegacyResponseLne(responseLine);

        var response = new GeminiResponse(url, responseLine);
        if (responseBytes.Length > endIndex + 2)
        {
            //the rest of the byte array is the body
            response.BodyBytes = responseBytes.Skip(endIndex + 2).ToArray();
        }

        return response;
    }

    public static byte[] CreateRequestBytes(GeminiUrl url)
        => ToBytes($"{url}\r\n");

    public static byte[] CreateResponseBytes(GeminiResponse geminiResponse)
          => CreateResponseBytes(geminiResponse.StatusCode, geminiResponse.Meta, geminiResponse.BodyBytes);

    public static byte[] CreateResponseBytes(int statusCode, string meta, byte[]? bodyBytes)
    {
        byte[] fullResponseBytes = ToBytes($"{statusCode} {meta}\r\n");

        if (bodyBytes != null && bodyBytes.Length > 0)
        {
            fullResponseBytes = fullResponseBytes.Concat(bodyBytes).ToArray();
        }
        return fullResponseBytes;
    }

    /// <summary>
    /// Gets a hash of the entire response. Used when looking to see if a URL's contents have changed
    /// </summary>
    /// <param name="bytes"></param>
    /// <returns></returns>
    public static string GetStrongHash(byte[] bytes)
        => "sha256:" + Convert.ToHexString(SHA256.HashData(bytes)).ToLower();

    /// <summary>
    /// Gets a hash of the entire response. Used when looking to see if a URL's contents have changed
    /// </summary>
    /// <param name="bytes"></param>
    /// <returns></returns>
    public static string GetStrongHash(GeminiResponse response)
        => GetStrongHash(CreateResponseBytes(response));

    private static byte[] ToBytes(string s)
        => Encoding.UTF8.GetBytes(s);

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

