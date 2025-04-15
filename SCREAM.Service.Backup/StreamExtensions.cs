namespace SCREAM.Service.Backup
{
    public static class StreamExtensions
    {
        public static async Task<int> ReadAtLeastAsync(this Stream stream, Memory<byte> memory, CancellationToken cancellationToken = default)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            var totalBytesRead = 0;
            while (totalBytesRead < memory.Length)
            {
                int bytesRead = await stream.ReadAsync(memory.Slice(totalBytesRead), cancellationToken);
                if (bytesRead == 0) break;  // We've reached the end of the stream.
                totalBytesRead += bytesRead;
            }
            return totalBytesRead;
        }
    }
}
