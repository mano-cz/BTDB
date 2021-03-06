using System;
using System.Collections.Generic;
using System.Linq;
using BTDB.Buffer;
using BTDB.EventStoreLayer;
using NUnit.Framework;

namespace BTDBTest
{
    [TestFixture]
    public class EventStoreTest
    {
        [Test]
        public void CanWriteSimpleEvent()
        {
            var manager = new EventStoreManager();
            var appender = manager.AppendToStore(new MemoryEventFileStorage());
            appender.Store(null, new object[] { 1 });
            var eventObserver = new StoringEventObserver();
            appender.ReadFromStartToEnd(eventObserver);
            Assert.AreEqual(new object[] { null }, eventObserver.Metadata);
            Assert.AreEqual(new[] { new object[] { 1 } }, eventObserver.Events);
            Assert.True(appender.IsKnownAsAppendable());
            Assert.False(appender.IsKnownAsCorrupted());
            Assert.False(appender.IsKnownAsFinished());
            Assert.AreEqual(10, appender.KnownAppendablePosition());
        }

        [Test]
        public void CanFinalizeEventStore()
        {
            var manager = new EventStoreManager();
            var appender = manager.AppendToStore(new MemoryEventFileStorage());
            appender.FinalizeStore();
            Assert.False(appender.IsKnownAsAppendable());
            Assert.False(appender.IsKnownAsCorrupted());
            Assert.True(appender.IsKnownAsFinished());
        }

        [Test]
        public void CanFinalizeEventStoreAfterReadFromStart()
        {
            var manager = new EventStoreManager();
            manager.CompressionStrategy = new NoCompressionStrategy();
            var file1 = new MemoryEventFileStorage(4096, 4096);
            var appender = manager.AppendToStore(file1);
            appender.Store(null, new object[] { new byte[4000] });
            appender.Store(null, new object[] { new byte[4000] });
            Assert.AreNotSame(file1, appender.CurrentFileStorage);
            var reader = manager.OpenReadOnlyStore(file1);
            reader.ReadFromStartToEnd(new SkippingEventObserver());
            Assert.False(reader.IsKnownAsAppendable());
            Assert.False(reader.IsKnownAsCorrupted());
            Assert.True(reader.IsKnownAsFinished());
            Assert.True(appender.IsKnownAsAppendable());
            Assert.False(appender.IsKnownAsCorrupted());
            Assert.False(appender.IsKnownAsFinished());
        }

        [Test]
        public void CanReadLongerEventsFromIncompleteFile()
        {
            var manager = new EventStoreManager();
            manager.CompressionStrategy = new NoCompressionStrategy();
            var file1 = new MemoryEventFileStorage();
            var appender = manager.AppendToStore(file1);
            appender.Store(null, new object[] { new byte[8000] });
            var file2 = new MemoryEventFileStorage();
            var buf = ByteBuffer.NewSync(new byte[4096]);
            file1.Read(buf, 0);
            file2.Write(buf, 0);
            var reader = manager.OpenReadOnlyStore(file2);
            reader.ReadFromStartToEnd(new SkippingEventObserver());
            Assert.False(reader.IsKnownAsCorrupted());
            Assert.False(reader.IsKnownAsAppendable());
            Assert.False(reader.IsKnownAsFinished());
            buf = ByteBuffer.NewSync(new byte[4096]);
            file1.Read(buf, 4096);
            file2.Write(buf, 4096);
            reader.ReadToEnd(new SkippingEventObserver());
            Assert.False(reader.IsKnownAsCorrupted());
            Assert.True(reader.IsKnownAsAppendable());
            Assert.False(reader.IsKnownAsFinished());
        }

        [Test]
        public void CanFinalizeEventStoreOnEverySectorPosition()
        {
            for (int i = 4000; i < 4600; i++)
            {
                var manager = new EventStoreManager();
                manager.CompressionStrategy = new NoCompressionStrategy();
                var file1 = new MemoryEventFileStorage(4096, 8192);
                var appender = manager.AppendToStore(file1);
                appender.Store(null, new object[] { new byte[i] });
                appender.Store(null, new object[] { new byte[7000] });
            }
        }

        [Test]
        public void CanFinalizeEventStoreAfterReadFromStartFirstNearlyFull()
        {
            var manager = new EventStoreManager();
            manager.CompressionStrategy = new NoCompressionStrategy();
            var file1 = new MemoryEventFileStorage(4096, 4096);
            var appender = manager.AppendToStore(file1);
            appender.Store(null, new object[] { new byte[4080] });
            appender.Store(null, new object[] { new byte[4000] });
            Assert.AreNotSame(file1, appender.CurrentFileStorage);
            var reader = manager.OpenReadOnlyStore(file1);
            reader.ReadFromStartToEnd(new SkippingEventObserver());
            Assert.False(reader.IsKnownAsAppendable());
            Assert.False(reader.IsKnownAsCorrupted());
            Assert.True(reader.IsKnownAsFinished());
            Assert.True(appender.IsKnownAsAppendable());
            Assert.False(appender.IsKnownAsCorrupted());
            Assert.False(appender.IsKnownAsFinished());
        }

        [Test]
        public void CreatesNewFileWhenOldOneIsFull()
        {
            var manager = new EventStoreManager();
            var appender = manager.AppendToStore(new MemoryEventFileStorage());
            appender.Store(null, new object[] { 1 });
            appender.ReadFromStartToEnd(new SkippingEventObserver());
            appender.FinalizeStore();
            Assert.False(appender.IsKnownAsAppendable());
            Assert.False(appender.IsKnownAsCorrupted());
            Assert.True(appender.IsKnownAsFinished());

        }

        class StoringEventObserver : IEventStoreObserver
        {
            public readonly List<object> Metadata = new List<object>();
            public readonly List<object[]> Events = new List<object[]>();
            uint _lastEventCount;

            public bool ObservedMetadata(object metadata, uint eventCount)
            {
                Metadata.Add(metadata);
                _lastEventCount = eventCount;
                return true;
            }

            public virtual bool ShouldStopReadingNextEvents()
            {
                return false;
            }

            public void ObservedEvents(object[] events)
            {
                Assert.AreEqual(_lastEventCount, events.Length);
                Events.Add(events);
            }
        }

        class StoringEventObserverWithStop : StoringEventObserver
        {
            public override bool ShouldStopReadingNextEvents()
            {
                return true;
            }
        }

        public class User : IEquatable<User>
        {
            public string Name { get; set; }
            public int Age { get; set; }

            public bool Equals(User other)
            {
                if (other == null) return false;
                return Name == other.Name && Age == other.Age;
            }
        }

        [Test]
        public void CanWriteMultipleEventsWithMetadata()
        {
            var manager = new EventStoreManager();
            var appender = manager.AppendToStore(new MemoryEventFileStorage());
            var metadata = new User { Name = "A", Age = 1 };
            var events = new object[]
                {
                    new User {Name = "B", Age = 2},
                    new User {Name = "C", Age = 3}
                };
            appender.Store(metadata, events);
            var eventObserver = new StoringEventObserver();
            appender.ReadFromStartToEnd(eventObserver);
            Assert.AreEqual(new object[] { metadata }, eventObserver.Metadata);
            Assert.AreEqual(new[] { events }, eventObserver.Events);
        }

        [Test]
        public void CanWriteSimpleEventAndReadItIndependently()
        {
            var manager = new EventStoreManager();
            manager.SetNewTypeNameMapper(new SimplePersonTypeMapper());
            var file = new MemoryEventFileStorage();
            var appender = manager.AppendToStore(file);
            var user = new User { Name = "A", Age = 1 };
            appender.Store(null, new object[] { user });

            manager = new EventStoreManager();
            manager.SetNewTypeNameMapper(new SimplePersonTypeMapper());
            var reader = manager.OpenReadOnlyStore(file);
            var eventObserver = new StoringEventObserver();
            reader.ReadFromStartToEnd(eventObserver);
            Assert.AreEqual(new object[] { null }, eventObserver.Metadata);
            Assert.AreEqual(new[] { new object[] { user } }, eventObserver.Events);
        }

        [Test]
        public void CustomEventIsReadFromSecondSplit()
        {
            var manager = new EventStoreManager();
            manager.CompressionStrategy = new NoCompressionStrategy();
            manager.SetNewTypeNameMapper(new SimplePersonTypeMapper());
            var appender = manager.AppendToStore(new MemoryEventFileStorage(4096, 4096));
            var first = appender.CurrentFileStorage;
            var user = new User { Name = "A", Age = 1 };
            while (appender.CurrentFileStorage == first)
                appender.Store(null, new object[] { user });
            var second = appender.CurrentFileStorage;

            manager = new EventStoreManager();
            manager.CompressionStrategy = new NoCompressionStrategy();
            manager.SetNewTypeNameMapper(new SimplePersonTypeMapper());
            var reader = manager.OpenReadOnlyStore(second);
            var eventObserver = new StoringEventObserver();
            reader.ReadFromStartToEnd(eventObserver);
            Assert.AreEqual(new object[] { null }, eventObserver.Metadata);
            Assert.AreEqual(new[] { new object[] { user } }, eventObserver.Events);
        }


        public class SimplePersonTypeMapper : ITypeNameMapper
        {
            public string ToName(Type type)
            {
                if (type == typeof(User)) return "User";
                throw new ArgumentOutOfRangeException();
            }

            public Type ToType(string name)
            {
                if (name == "User") return typeof(User);
                throw new ArgumentOutOfRangeException();
            }
        }

        public class UserEvent : IEquatable<UserEvent>
        {
            public long Id { get; set; }
            public User User1 { get; set; }
            public User User2 { get; set; }

            public bool Equals(UserEvent other)
            {
                if (Id != other.Id) return false;
                if (User1 != other.User1 && (User1 == null || !User1.Equals(other.User1))) return false;
                return User2 == other.User2 || (User2 != null && User2.Equals(other.User2));
            }
        }

        [Test]
        public void NestedObjectsTest()
        {
            var manager = new EventStoreManager();
            var file = new MemoryEventFileStorage();
            var appender = manager.AppendToStore(file);
            var userEvent = new UserEvent { Id = 10, User1 = new User { Name = "A", Age = 1 } };
            appender.Store(null, new object[] { userEvent });

            manager = new EventStoreManager();
            var reader = manager.OpenReadOnlyStore(file);
            var eventObserver = new StoringEventObserver();
            reader.ReadFromStartToEnd(eventObserver);
            Assert.AreEqual(new object[] { null }, eventObserver.Metadata);
            Assert.AreEqual(new[] { new object[] { userEvent } }, eventObserver.Events);
        }

        [Test]
        public void SameReferenceTest()
        {
            var manager = new EventStoreManager();
            var file = new MemoryEventFileStorage();
            var appender = manager.AppendToStore(file);
            var user = new User { Name = "A", Age = 1 };
            var userEvent = new UserEvent { Id = 10, User1 = user, User2 = user };
            appender.Store(null, new object[] { userEvent });

            manager = new EventStoreManager();
            var reader = manager.OpenReadOnlyStore(file);
            var eventObserver = new StoringEventObserver();
            reader.ReadFromStartToEnd(eventObserver);
            var readUserEvent = (UserEvent)eventObserver.Events[0][0];
            Assert.AreSame(readUserEvent.User1, readUserEvent.User2);
        }

        public class UserEventMore : IEquatable<UserEventMore>
        {
            public long Id { get; set; }
            public User User1 { get; set; }
            public User User2 { get; set; }
            public User User3 { get; set; }

            public bool Equals(UserEventMore other)
            {
                if (Id != other.Id) return false;
                if (User1 != other.User1 && (User1 == null || !User1.Equals(other.User1))) return false;
                if (User2 != other.User2 && (User2 == null || !User2.Equals(other.User2))) return false;
                return User3 == other.User3 || (User3 != null && User3.Equals(other.User3));
            }
        }

        public class OverloadableTypeMapper : ITypeNameMapper
        {
            readonly ITypeNameMapper _parent = new FullNameTypeMapper();
            readonly Type _type;
            readonly string _name;

            public OverloadableTypeMapper(Type type, string name)
            {
                _type = type;
                _name = name;
            }

            public string ToName(Type type)
            {
                if (type == _type) return _name;
                return _parent.ToName(type);
            }

            public Type ToType(string name)
            {
                if (name == _name) return _type;
                return _parent.ToType(name);
            }
        }

        [Test]
        public void UpgradeToMoreObjectProperties()
        {
            var manager = new EventStoreManager();
            manager.SetNewTypeNameMapper(new OverloadableTypeMapper(typeof(UserEvent), "UserEvent"));
            var file = new MemoryEventFileStorage();
            var appender = manager.AppendToStore(file);
            var user = new User { Name = "A", Age = 1 };
            var userEvent = new UserEvent { Id = 10, User1 = user, User2 = user };
            appender.Store(null, new object[] { userEvent, new User { Name = "B" } });

            manager = new EventStoreManager();
            manager.SetNewTypeNameMapper(new OverloadableTypeMapper(typeof(UserEventMore), "UserEvent"));
            var reader = manager.OpenReadOnlyStore(file);
            var eventObserver = new StoringEventObserver();
            reader.ReadFromStartToEnd(eventObserver);
            var readUserEvent = (UserEventMore)eventObserver.Events[0][0];
            Assert.AreSame(readUserEvent.User1, readUserEvent.User2);
            Assert.AreEqual("A", readUserEvent.User1.Name);
            Assert.AreEqual(10, readUserEvent.Id);
            Assert.IsNull(readUserEvent.User3);
            Assert.AreEqual("B", ((User)eventObserver.Events[0][1]).Name);
        }

        public class UserEventLess : IEquatable<UserEventLess>
        {
            public long Id { get; set; }
            public User User2 { get; set; }

            public bool Equals(UserEventLess other)
            {
                if (Id != other.Id) return false;
                return User2 == other.User2 || (User2 != null && User2.Equals(other.User2));
            }
        }

        [Test]
        public void UpgradeToLessObjectProperties()
        {
            var manager = new EventStoreManager();
            manager.SetNewTypeNameMapper(new OverloadableTypeMapper(typeof(UserEvent), "UserEvent"));
            var file = new MemoryEventFileStorage();
            var appender = manager.AppendToStore(file);
            var user = new User { Name = "A", Age = 1 };
            var userEvent = new UserEvent { Id = 10, User1 = user, User2 = user };
            appender.Store(null, new object[] { userEvent, new User { Name = "B" } });

            manager = new EventStoreManager();
            manager.SetNewTypeNameMapper(new OverloadableTypeMapper(typeof(UserEventLess), "UserEvent"));
            var reader = manager.OpenReadOnlyStore(file);
            var eventObserver = new StoringEventObserver();
            reader.ReadFromStartToEnd(eventObserver);
            var readUserEvent = (UserEventLess)eventObserver.Events[0][0];
            Assert.AreEqual("A", readUserEvent.User2.Name);
            Assert.AreEqual(10, readUserEvent.Id);
            Assert.AreEqual("B", ((User)eventObserver.Events[0][1]).Name);
        }

        public class UserEventList : IEquatable<UserEventList>
        {
            public long Id { get; set; }
            public List<User> List { get; set; }

            public bool Equals(UserEventList other)
            {
                if (Id != other.Id) return false;
                if (List == other.List) return true;
                if (List == null || other.List == null) return false;
                if (List.Count != other.List.Count) return false;
                return List.Zip(other.List, (u1, u2) => u1 == u2 || (u1 != null && u1.Equals(u2))).All(b => b);
            }
        }

        [Test]
        public void SupportsList()
        {
            var manager = new EventStoreManager();
            var file = new MemoryEventFileStorage();
            var appender = manager.AppendToStore(file);
            var userA = new User { Name = "A", Age = 1 };
            var userB = new User { Name = "B", Age = 2 };
            var userEvent = new UserEventList { Id = 10, List = new List<User> { userA, userB, userA } };
            appender.Store(null, new object[] { userEvent });

            manager = new EventStoreManager();
            var reader = manager.OpenReadOnlyStore(file);
            var eventObserver = new StoringEventObserver();
            reader.ReadFromStartToEnd(eventObserver);
            var readUserEvent = (UserEventList)eventObserver.Events[0][0];
            Assert.AreEqual(readUserEvent, userEvent);
        }

        [Test]
        public void SkipListOnUpgrade()
        {
            var manager = new EventStoreManager();
            manager.SetNewTypeNameMapper(new OverloadableTypeMapper(typeof(UserEventList), "UserEvent"));
            var file = new MemoryEventFileStorage();
            var appender = manager.AppendToStore(file);
            var userA = new User { Name = "A", Age = 1 };
            var userB = new User { Name = "B", Age = 2 };
            var userEvent = new UserEventList { Id = 10, List = new List<User> { userA, userB, userA } };
            appender.Store(null, new object[] { userEvent });

            manager = new EventStoreManager();
            manager.SetNewTypeNameMapper(new OverloadableTypeMapper(typeof(UserEvent), "UserEvent"));
            var reader = manager.OpenReadOnlyStore(file);
            var eventObserver = new StoringEventObserver();
            reader.ReadFromStartToEnd(eventObserver);
            var readUserEvent = (UserEvent)eventObserver.Events[0][0];
            Assert.AreEqual(10, readUserEvent.Id);
            Assert.IsNull(readUserEvent.User1);
        }

        public class UserEventDictionary : IEquatable<UserEventDictionary>
        {
            public long Id { get; set; }
            public Dictionary<string, User> Dict { get; set; }

            public bool Equals(UserEventDictionary other)
            {
                if (Id != other.Id) return false;
                if (Dict == other.Dict) return true;
                if (Dict == null || other.Dict == null) return false;
                if (Dict.Count != other.Dict.Count) return false;
                foreach (var p in Dict)
                {
                    User u;
                    if (!other.Dict.TryGetValue(p.Key, out u)) return false;
                    if (p.Value != u && (p.Value == null || !p.Value.Equals(u))) return false;
                }
                return true;
            }
        }

        [Test]
        public void SupportsDictionary()
        {
            var manager = new EventStoreManager();
            var file = new MemoryEventFileStorage();
            var appender = manager.AppendToStore(file);
            var userA = new User { Name = "A", Age = 1 };
            var userB = new User { Name = "B", Age = 2 };
            var userEvent = new UserEventDictionary { Id = 10, Dict = new Dictionary<string, User> { { "A", userA }, { "B", userB } } };
            appender.Store(null, new object[] { userEvent });

            manager = new EventStoreManager();
            var reader = manager.OpenReadOnlyStore(file);
            var eventObserver = new StoringEventObserver();
            reader.ReadFromStartToEnd(eventObserver);
            var readUserEvent = (UserEventDictionary)eventObserver.Events[0][0];
            Assert.AreEqual(readUserEvent, userEvent);
        }

        [Test]
        public void SupportsDataOverMaxBlockSize()
        {
            var manager = new EventStoreManager();
            var file = new MemoryEventFileStorage();
            var appender = manager.AppendToStore(file);
            var randomData = new byte[20000];
            new Random().NextBytes(randomData);
            appender.Store(null, new object[] { randomData });
            Assert.Less(10000, appender.KnownAppendablePosition());

            manager = new EventStoreManager();
            var reader = manager.OpenReadOnlyStore(file);
            var eventObserver = new StoringEventObserver();
            reader.ReadFromStartToEnd(eventObserver);
            Assert.AreEqual(new object[] { null }, eventObserver.Metadata);
            Assert.AreEqual(new[] { new object[] { randomData } }, eventObserver.Events);
        }

        [Test]
        public void CompressionShortensData()
        {
            var manager = new EventStoreManager();
            var file = new MemoryEventFileStorage();
            var appender = manager.AppendToStore(file);
            var compressibleData = new byte[20000];
            appender.Store(null, new object[] { compressibleData });
            Assert.Greater(2000, appender.KnownAppendablePosition());

            manager = new EventStoreManager();
            var reader = manager.OpenReadOnlyStore(file);
            var eventObserver = new StoringEventObserver();
            reader.ReadFromStartToEnd(eventObserver);
            Assert.AreEqual(new object[] { null }, eventObserver.Metadata);
            Assert.AreEqual(new[] { new object[] { compressibleData } }, eventObserver.Events);
        }

        [Test]
        public void CanStopReadBatchesAfterFirst()
        {
            var manager = new EventStoreManager();
            var file = new MemoryEventFileStorage();
            var appender = manager.AppendToStore(file);
            var metadata = new User { Name = "A", Age = 1 };
            var events = new object[]
                {
                    new User {Name = "B", Age = 2},
                    new User {Name = "C", Age = 3}
                };
            appender.Store(metadata, events);
            appender.Store(metadata, events);

            var reader = manager.OpenReadOnlyStore(file);
            var eventObserver = new StoringEventObserverWithStop();
            reader.ReadFromStartToEnd(eventObserver);
            Assert.False(reader.IsKnownAsCorrupted());
            Assert.False(reader.IsKnownAsFinished());
            Assert.False(reader.IsKnownAsAppendable());
            Assert.AreEqual(new object[] { metadata }, eventObserver.Metadata);
            Assert.AreEqual(new[] { events }, eventObserver.Events);
        }

        [Test]
        public void MoreComplexRepeatedAppendingAndReading()
        {
            var manager = new EventStoreManager();
            for (var i = 490; i < 520; i += 2)
            {
                var file = new MemoryEventFileStorage();
                var appender = manager.AppendToStore(file);
                appender.Store(null, new object[] { new byte[i] });
                var eventObserver = new StoringEventObserver();
                appender.ReadFromStartToEnd(eventObserver);
                appender.Store(null, new object[] { new byte[i] });
                appender.ReadFromStartToEnd(eventObserver);
            }
        }

        public class SpecificList
        {
            public List<ulong> Ulongs { get; set; }
        }

        public class SpecificDictIList
        {
            public IDictionary<ulong, IList<ulong>> Dict { get; set; }
        }

        [Test]
        public void MixedIListAndList()
        {
            var manager = new EventStoreManager();
            var file = new MemoryEventFileStorage();
            var appender = manager.AppendToStore(file);
            var e1 = new SpecificDictIList { Dict = new Dictionary<ulong, IList<ulong>>() };
            var e2 = new SpecificList { Ulongs = new List<ulong>() };
            appender.Store(null, new object[] { e2 });
            appender.Store(null, new object[] { e1 });

            manager = new EventStoreManager();
            appender = manager.AppendToStore(file);
            var eventObserver = new StoringEventObserver();
            appender.ReadFromStartToEnd(eventObserver);
            appender.Store(null, new object[] { e1 });
        }

        public class UsersIList
        {
            public IList<User> Users { get; set; }
        }

        [Test]
        public void UseArrayForStoreIList()
        {
            var manager = new EventStoreManager();
            var file = new MemoryEventFileStorage();
            var appender = manager.AppendToStore(file);
            var e = new UsersIList { Users = new[] { new User { Name = "A" }, new User { Name = "B" } } };
            appender.Store(null, new object[] { e });
        }
    }
}
