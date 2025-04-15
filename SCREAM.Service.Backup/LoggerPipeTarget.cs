using CliWrap;

namespace SCREAM.Service.Backup
{
    /// <summary>
    /// A custom PipeTarget that logs each line of error output from a command's standard error stream.
    /// </summary>
    public class LoggerPipeTarget : PipeTarget
    {
        private readonly ILogger _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="LoggerPipeTarget"/> class.
        /// </summary>
        /// <param name="logger">The logger instance used to log error output.</param>
        public LoggerPipeTarget(ILogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Reads the error output stream and logs every line.
        /// This method overrides the abstract CopyFromAsync method from PipeTarget.
        /// </summary>
        /// <param name="source">The source stream (typically the standard error stream) from which to read data.</param>
        /// <param name="cancellationToken">A cancellation token to cancel the asynchronous operation.</param>
        /// <returns>A task that represents the asynchronous copy operation.</returns>
        public override async Task CopyFromAsync(Stream source, CancellationToken cancellationToken = default)
        {
            using var reader = new StreamReader(source);
            while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync();
                if (!string.IsNullOrEmpty(line))
                {
                    _logger.LogError(line);
                }
            }
        }
    }
}
