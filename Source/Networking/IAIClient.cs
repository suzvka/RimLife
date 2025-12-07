using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace RimLife.Networking
{
    public interface IAIClient
    {
        Task<string> SendStringAsync(string text);
        Task<string> SendStringStreamingAsync(string text, Action<string> onChunkReceived);
    }
}
