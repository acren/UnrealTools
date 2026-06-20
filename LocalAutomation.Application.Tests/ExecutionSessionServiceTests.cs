using System;
using System.Threading.Tasks;
using LocalAutomation.Runtime;
using Microsoft.Extensions.Logging;
using Serilog.Events;
using TestUtilities;
using Xunit;

namespace LocalAutomation.Application.Tests;

public sealed class ExecutionSessionServiceTests
{
    /// <summary>
    /// Confirms that a background execution-session exception is forwarded to the session logger so the execution tab and
    /// per-session log file can surface the runtime failure.
    /// </summary>
    [Fact]
    public async Task BackgroundExecutionExceptionIsLoggedToSessionLogger()
    {
        ExecutionSessionService service = new();
        InvalidOperationException syntheticException = new("Synthetic background failure.");
        TaskCompletionSource<LogEvent> sessionErrorSource = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /* Inject the synthetic exception through requirements evaluation so the failure escapes ExecutionSession.Run()
           before any scheduler task-level logging can handle it. That isolates the session-service session-log path
           without needing a one-off operation type. */
        Operation operation = new ExecutionTestCommon.InlineOperation(
            operationName: "Application Test Operation",
            checkRequirements: _ => throw syntheticException);
        OperationParameters parameters = operation.CreateParameters();
        parameters.Target = new ExecutionTestCommon.TestTarget();

        /* Capture the first session-scoped error event that carries the service-level background failure message. */
        void CaptureSessionError(LogEvent entry)
        {
            string renderedMessage = entry.RenderMessage();
            if (entry.Level >= LogEventLevel.Error && renderedMessage.Contains("Execution session", StringComparison.Ordinal))
            {
                sessionErrorSource.TrySetResult(entry);
            }
        }

        /* Attach the session-log observer before execution starts so the fire-and-forget background path cannot emit the
           session-service error before the test is listening. */
        LocalAutomation.Runtime.ExecutionSession session = service.StartExecution(operation, parameters, createdSession =>
        {
            createdSession.LogStream.EntryAdded += CaptureSessionError;
        });

        try
        {
            LogEvent entry = await sessionErrorSource.Task.WaitAsync(TimeSpan.FromSeconds(5));
            string renderedMessage = entry.RenderMessage();

            /* The session logger keeps the original structured event, so the test asserts on event level, structured
               session/task properties, and the rendered failure text seen by buffered session observers. */
            Assert.Equal(LogEventLevel.Error, entry.Level);
            Assert.True(entry.Properties.TryGetValue("SessionId", out LogEventPropertyValue? sessionIdProperty));
            Assert.Equal(session.Id.Value, Assert.IsType<ScalarValue>(sessionIdProperty).Value);
            Assert.False(entry.Properties.ContainsKey("TaskId"));
            Assert.Contains("Execution session", renderedMessage, StringComparison.Ordinal);
            Assert.Contains("Synthetic background failure.", renderedMessage, StringComparison.Ordinal);
        }
        finally
        {
            session.LogStream.EntryAdded -= CaptureSessionError;
        }
    }
}
