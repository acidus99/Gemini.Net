﻿using System;
namespace Gemini.Net
{
    public class FoundLink : IEquatable<FoundLink>
    {
        public GeminiUrl Url { get; set; }
        public bool IsExternal { get; set; }
        public string LinkText { get; set; }

        /// <summary>
        /// What makes a FoundLink unique is really just its URL.
        /// </summary>
        /// <param name="other"></param>
        /// <returns></returns>
        public bool Equals(FoundLink other)
            => other != null && Url.Equals(other.Url);

        public override bool Equals(object obj)
            => Equals(obj as GeminiUrl);

        public override int GetHashCode()
            => Url.GetHashCode();

    }
}
