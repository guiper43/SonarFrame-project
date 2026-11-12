using System.IO;
using System.Runtime.InteropServices;
using LibVLCSharp.Shared;

namespace SonarFrame.Desktop;

internal sealed class EntradaComposicaoVlc : MediaInput
{
    private readonly Stream fluxo;
    private readonly byte[] buffer = new byte[32768];
    public EntradaComposicaoVlc(Stream fluxo) { this.fluxo = fluxo; CanSeek = true; }
    public override bool Open(out ulong size) { size = ulong.MaxValue; return true; }
    public override int Read(IntPtr buf, uint len)
    {
        try
        {
            var quantidade = fluxo.Read(buffer, 0, (int)Math.Min(len, (uint)buffer.Length));
            Marshal.Copy(buffer, 0, buf, quantidade);
            return quantidade;
        }
        catch (Exception erro) when (erro is IOException or OperationCanceledException or ObjectDisposedException) { return -1; }
    }
    public override bool Seek(ulong offset)
    {
        try { if (offset > (ulong)fluxo.Length) return false; fluxo.Position = checked((long)offset); return true; }
        catch (Exception erro) when (erro is IOException or ObjectDisposedException) { return false; }
    }
    public override void Close() { }
    protected override void Dispose(bool disposing) { if (disposing) fluxo.Dispose(); base.Dispose(disposing); }
}
