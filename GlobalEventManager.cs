using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SimioEventCommunication
{
    // Shared event data structure
    public class SimulationEvent
    {
        public double Timestamp { get; set; }
        public string VariableName { get; set; }
        public double OldValue { get; set; }
        public double NewValue { get; set; }
        public string EventType { get; set; }
        public string ModelName { get; set; }
        public DateTime SystemTime { get; set; } = DateTime.Now;
    }

    // Interface for event subscribers (dashboard)
    public interface IEventSubscriber
    {
        void ProcessEventBatch(List<SimulationEvent> events);
    }

    // Global static event manager - accessible from anywhere
    public static class GlobalEventManager
    {
        private static readonly ConcurrentQueue<SimulationEvent> _globalEventQueue = new ConcurrentQueue<SimulationEvent>();
        private static readonly List<IEventSubscriber> _subscribers = new List<IEventSubscriber>();
        private static readonly object _subscriberLock = new object();
        private static volatile bool _isProcessorRunning = false;
        private static CancellationTokenSource _cancellationTokenSource;
        private static Task _eventProcessorTask;

        // Statistics
        public static long TotalEventsProcessed { get; private set; }
        public static long EventsInQueue => _globalEventQueue.Count;
        public static DateTime LastEventTime { get; private set; }

        // Configuration
        public static int MaxQueueSize { get; set; } = 50000;
        public static int ProcessingBatchSize { get; set; } = 500;
        public static int ProcessingIntervalMs { get; set; } = 10;

        // Simple logging for debugging
        public static bool EnableDebugLogging { get; set; } = true;

        static GlobalEventManager()
        {
            if (EnableDebugLogging)
            {
                System.Diagnostics.Debug.WriteLine("GlobalEventManager: Static constructor called");
            }
        }

        public static void LogEvent(SimulationEvent eventData)
        {
            try
            {
                if (eventData == null) return;
                // ADD THIS LINE RIGHT HERE
                System.Diagnostics.Debug.WriteLine($"DEBUG: GlobalEventManager.LogEvent called with {eventData.VariableName} = {eventData.NewValue}, Subscribers: {_subscribers.Count}");
                // Prevent queue overflow
                if (_globalEventQueue.Count >= MaxQueueSize)
                {
                    // Remove oldest events to make room
                    for (int i = 0; i < ProcessingBatchSize && _globalEventQueue.TryDequeue(out _); i++) { }

                    if (EnableDebugLogging)
                    {
                        System.Diagnostics.Debug.WriteLine($"GlobalEventManager: Queue overflow, removed {ProcessingBatchSize} old events");
                    }
                }

                _globalEventQueue.Enqueue(eventData);
                LastEventTime = DateTime.Now;

                if (EnableDebugLogging && TotalEventsProcessed % 100 == 0)
                {
                    System.Diagnostics.Debug.WriteLine($"GlobalEventManager: Logged event #{TotalEventsProcessed}, Queue size: {_globalEventQueue.Count}");
                }

                EnsureProcessorRunning();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GlobalEventManager.LogEvent error: {ex.Message}");
            }
        }

        public static void Subscribe(IEventSubscriber subscriber)
        {
            try
            {
                lock (_subscriberLock)
                {
                    if (!_subscribers.Contains(subscriber))
                    {
                        _subscribers.Add(subscriber);
                        // ADD THIS LINE/ ADD THIS LINE
                        System.Diagnostics.Debug.WriteLine($"DEBUG: Subscriber added! Total subscribers: {_subscribers.Count}");
                        if (EnableDebugLogging)
                        {
                            System.Diagnostics.Debug.WriteLine($"GlobalEventManager: Subscriber added. Total subscribers: {_subscribers.Count}");
                        }
                        EnsureProcessorRunning();
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GlobalEventManager.Subscribe error: {ex.Message}");
            }
        }

        public static void Unsubscribe(IEventSubscriber subscriber)
        {
            try
            {
                lock (_subscriberLock)
                {
                    _subscribers.Remove(subscriber);
                    if (EnableDebugLogging)
                    {
                        System.Diagnostics.Debug.WriteLine($"GlobalEventManager: Subscriber removed. Total subscribers: {_subscribers.Count}");
                    }

                    // Stop processor if no subscribers
                    if (_subscribers.Count == 0)
                    {
                        StopProcessor();
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GlobalEventManager.Unsubscribe error: {ex.Message}");
            }
        }

        private static void EnsureProcessorRunning()
        {
            if (!_isProcessorRunning)
            {
                lock (_subscriberLock)
                {
                    if (!_isProcessorRunning && _subscribers.Count > 0)
                    {
                        _cancellationTokenSource = new CancellationTokenSource();
                        _eventProcessorTask = Task.Run(() => ProcessEvents(_cancellationTokenSource.Token));
                        _isProcessorRunning = true;

                        if (EnableDebugLogging)
                        {
                            System.Diagnostics.Debug.WriteLine("GlobalEventManager: Event processor started");
                        }
                    }
                }
            }
        }

        private static void StopProcessor()
        {
            if (_isProcessorRunning)
            {
                try
                {
                    _cancellationTokenSource?.Cancel();
                    _eventProcessorTask?.Wait(1000);
                    _isProcessorRunning = false;

                    if (EnableDebugLogging)
                    {
                        System.Diagnostics.Debug.WriteLine("GlobalEventManager: Event processor stopped");
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"GlobalEventManager.StopProcessor error: {ex.Message}");
                }
            }
        }

        private static async Task ProcessEvents(CancellationToken cancellationToken)
        {
            var eventBatch = new List<SimulationEvent>();

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    eventBatch.Clear();

                    // Collect batch of events
                    int eventsCollected = 0;
                    while (_globalEventQueue.TryDequeue(out SimulationEvent eventData) &&
                           eventsCollected < ProcessingBatchSize)
                    {
                        eventBatch.Add(eventData);
                        eventsCollected++;
                    }

                    // Distribute to subscribers if we have events
                    if (eventBatch.Count > 0)
                    {
                        lock (_subscriberLock)
                        {
                            foreach (var subscriber in _subscribers.ToList())
                            {
                                try
                                {
                                    subscriber.ProcessEventBatch(eventBatch);
                                }
                                catch (Exception ex)
                                {
                                    System.Diagnostics.Debug.WriteLine($"GlobalEventManager: Error in subscriber: {ex.Message}");
                                }
                            }
                        }

                        if (EnableDebugLogging && eventBatch.Count > 10)
                        {
                            System.Diagnostics.Debug.WriteLine($"GlobalEventManager: Processed batch of {eventBatch.Count} events to {_subscribers.Count} subscribers");
                        }
                    }
                    else
                    {
                        // No events, wait a bit
                        await Task.Delay(ProcessingIntervalMs, cancellationToken);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"GlobalEventManager.ProcessEvents error: {ex.Message}");
                    await Task.Delay(ProcessingIntervalMs * 5, cancellationToken);
                }
            }
        }

        public static void ClearEvents()
        {
            while (_globalEventQueue.TryDequeue(out _)) { }
            TotalEventsProcessed = 0;

            if (EnableDebugLogging)
            {
                System.Diagnostics.Debug.WriteLine("GlobalEventManager: Events cleared");
            }
        }

        public static void Shutdown()
        {
            try
            {
                StopProcessor();
                ClearEvents();
                lock (_subscriberLock)
                {
                    _subscribers.Clear();
                }

                if (EnableDebugLogging)
                {
                    System.Diagnostics.Debug.WriteLine("GlobalEventManager: Shutdown complete");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GlobalEventManager.Shutdown error: {ex.Message}");
            }
        }

        // Diagnostic methods
        public static string GetDiagnosticInfo()
        {
            return $"Events Processed: {TotalEventsProcessed}, Queue Size: {EventsInQueue}, Subscribers: {_subscribers.Count}, Processor Running: {_isProcessorRunning}";
        }
    }
}