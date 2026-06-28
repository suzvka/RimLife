using System;
using System.Collections.Generic;
using NPCLife.Core;
using NPCLife.Framework;
using RimLife.Infrastructure.Knowledge;
using Xunit;

namespace RimLife.Tests.Infrastructure
{
    /// <summary>
    /// MetricsKnowledgeService 断言测试。
    /// 验证装饰器正确代理所有 IKnowledgeService 方法，
    /// 并在 Lookup 时上报 RuntimeMetrics 指标。
    /// </summary>
    public class MetricsKnowledgeServiceTests
    {
        private readonly FakeKnowledgeService _inner = new FakeKnowledgeService();

        private MetricsKnowledgeService Create() => new MetricsKnowledgeService(_inner);

        // ================================================================
        // Constructor
        // ================================================================

        [Fact]
        public void Constructor_NullInner_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new MetricsKnowledgeService(null));
        }

        // ================================================================
        // Lookup delegation
        // ================================================================

        [Fact]
        public void Lookup_DelegatesToInner()
        {
            _inner.Entries.Add(new KnowledgeEntry
            {
                Term = "Raid", Definition = "attack", Source = "Test",
                Confidence = 1f, ContextTags = new List<string>()
            });

            var svc = Create();
            var results = svc.Lookup("Raid");

            Assert.Single(results);
            Assert.Equal("Raid", results[0].Term);
            Assert.Equal("Raid", _inner.LastLookupTerm);
        }

        [Fact]
        public void Lookup_RecordsMetrics_Hit()
        {
            RuntimeMetrics.Reset();
            var sessionId = RuntimeMetrics.BeginSession(AgentRole.Director);

            _inner.Entries.Add(new KnowledgeEntry
            {
                Term = "Raid", Definition = "attack", Source = "Test",
                Confidence = 1f, ContextTags = new List<string>()
            });

            var svc = Create();
            svc.Lookup("Raid");

            var snap = RuntimeMetrics.GetSnapshot();
            Assert.True(snap.Knowledge.Terms.Count > 0);
            Assert.Contains(snap.Knowledge.Terms, t => t.Term == "Raid" && t.HitCount > 0);

            RuntimeMetrics.EndSession(sessionId);
            RuntimeMetrics.Reset();
        }

        [Fact]
        public void Lookup_RecordsMetrics_Miss()
        {
            RuntimeMetrics.Reset();
            var sessionId = RuntimeMetrics.BeginSession(AgentRole.Director);

            var svc = Create();
            svc.Lookup("nonexistent");

            var snap = RuntimeMetrics.GetSnapshot();
            Assert.True(snap.Knowledge.Terms.Count > 0);
            Assert.Contains(snap.Knowledge.Terms, t => t.Term == "nonexistent" && t.HitCount == 0);

            RuntimeMetrics.EndSession(sessionId);
            RuntimeMetrics.Reset();
        }

        // ================================================================
        // Store / Delete / List* delegation
        // ================================================================

        [Fact]
        public void Store_DelegatesToInner()
        {
            var svc = Create();
            var entry = new KnowledgeEntry
            {
                Term = "Raid", Definition = "attack", Source = "Test",
                Confidence = 1f, ContextTags = new List<string>()
            };
            svc.Store(entry);

            Assert.Equal("Raid", _inner.LastStoredTerm);
        }

        [Fact]
        public void Delete_DelegatesToInner()
        {
            var svc = Create();
            svc.Delete("Raid");
            Assert.Equal("Raid", _inner.LastDeletedTerm);
        }

        [Fact]
        public void ListAll_DelegatesToInner()
        {
            _inner.Entries.Add(new KnowledgeEntry { Term = "A" });
            var svc = Create();
            Assert.Single(svc.ListAll());
        }

        [Fact]
        public void ListByTags_DelegatesToInner()
        {
            var svc = Create();
            svc.ListByTags(new List<string> { "Weapon" });
            Assert.NotNull(_inner.LastFilterTags);
            Assert.Contains("Weapon", _inner.LastFilterTags);
        }

        [Fact]
        public void ListByPrefix_DelegatesToInner()
        {
            var svc = Create();
            svc.ListByPrefix("A");
            Assert.Equal("A", _inner.LastFilterPrefix);
        }

        // ================================================================
        // Fake inner service
        // ================================================================

        private class FakeKnowledgeService : IKnowledgeService
        {
            public readonly List<KnowledgeEntry> Entries = new List<KnowledgeEntry>();
            public string LastLookupTerm;
            public string LastStoredTerm;
            public string LastDeletedTerm;
            public IReadOnlyList<string> LastFilterTags;
            public string LastFilterPrefix;

            public IReadOnlyList<KnowledgeEntry> Lookup(string term)
            {
                LastLookupTerm = term;
                var results = new List<KnowledgeEntry>();
                foreach (var e in Entries)
                    if (string.Equals(e.Term, term, StringComparison.OrdinalIgnoreCase))
                        results.Add(e);
                return results;
            }

            public void Store(KnowledgeEntry entry)
            {
                LastStoredTerm = entry?.Term;
                Entries.Add(entry);
            }

            public void Delete(string term) => LastDeletedTerm = term;
            public IReadOnlyList<KnowledgeEntry> ListAll() => Entries;

            public IReadOnlyList<KnowledgeEntry> ListByTags(IReadOnlyList<string> tags)
            {
                LastFilterTags = tags;
                return Entries;
            }

            public IReadOnlyList<KnowledgeEntry> ListByPrefix(string prefix)
            {
                LastFilterPrefix = prefix;
                return Entries;
            }
        }
    }
}
