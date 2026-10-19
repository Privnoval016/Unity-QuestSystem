using System.Diagnostics;
using System.Linq;
using NUnit.Framework;
using static QuestSystem.Tests.Build;

namespace QuestSystem.Tests
{
    /** <summary>Quests at the scale a full game reaches; the pass message carries the timings.</summary> */
    public sealed class QuestScaleTests
    {
        [Test]
        public void FiveHundredQuests_TwoThousandFlags()
        {
            using (var kit = new QuestKit())
            {
                var flags = Enumerable.Range(0, 2000).Select(i => kit.Flag("Flag" + i)).ToArray();
                var signals = Enumerable.Range(0, 50).Select(i => kit.Signal("Signal" + i)).ToArray();
                for (var q = 0; q < 500; q++)
                {
                    var steps = Enumerable.Range(0, 5).Select(s => s == 0
                        ? Step($"s{s}", Count($"o{q}-{s}", signals[(q + s) % signals.Length], 3))
                        : Step($"s{s}", new StepSettings { UnlockWhen = s == 4 ? new FlagCondition(flags[q * 4 % flags.Length]) : null }, Count($"o{q}-{s}", signals[(q + s) % signals.Length], 3)))
                        .ToArray();
                    kit.Quest("Quest" + q, steps);
                }
                var service = kit.Start();
                foreach (var quest in kit.Quests) service.Give(quest);

                var clock = Stopwatch.StartNew();
                for (var i = 0; i < 200; i++) service.Raise(signals[i % signals.Length]);
                var signalMs = clock.Elapsed.TotalMilliseconds / 200;
                clock.Restart();
                for (var i = 0; i < 200; i++) service.SetFlag(flags[i], 1);
                var flagMs = clock.Elapsed.TotalMilliseconds / 200;
                clock.Restart();
                var data = service.Capture();
                var captureMs = clock.Elapsed.TotalMilliseconds;
                clock.Restart();
                service.Restore(data);
                var restoreMs = clock.Elapsed.TotalMilliseconds;

                Assert.That(service.Quests.Count(quest => service.StateOf(quest) != QuestState.NotGiven), Is.EqualTo(500));
                Assert.Pass($"500 quests x 5 steps, 2000 flags: a signal {signalMs:F2} ms, a flag change {flagMs:F2} ms, capture {captureMs:F0} ms, restore {restoreMs:F0} ms");
            }
        }
    }
}
