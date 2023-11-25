namespace Scraper;

using System;
using System.Text;

public static class Extensions
{
    public static byte[] ToBytes(this string word) => Encoding.UTF8.GetBytes(word + Environment.NewLine);
}