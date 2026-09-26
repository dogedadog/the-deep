using System;
using Unity.Collections;
using Unity.Netcode;

namespace TheDeep.Data
{
    public enum DataCategory : byte { Creature, Geology, Environment, Structure, Evidence }

    public enum PacketStatus : byte
    {
        /// <summary>Arrived cleanly, waiting for the crew to log it.</summary>
        Pending,
        /// <summary>Arrived over a weak signal; needs the slower repair before it can be logged.</summary>
        Corrupted,
        Logging,
        Repairing,
        /// <summary>Ready to submit.</summary>
        Logged,
        Submitted,
    }

    /// <summary>One piece of scanned data, as it moves through the sub's terminal.</summary>
    public struct DataPacket : INetworkSerializable, IEquatable<DataPacket>
    {
        public int Id;
        public int TargetId;
        public int Value;
        public int Diver;          // crew number of the diver who sent it
        public DataCategory Category;
        public PacketStatus Status;
        public double FinishTime;  // server time when logging/repair completes
        public FixedString64Bytes Title;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Id);
            serializer.SerializeValue(ref TargetId);
            serializer.SerializeValue(ref Value);
            serializer.SerializeValue(ref Diver);
            serializer.SerializeValue(ref Category);
            serializer.SerializeValue(ref Status);
            serializer.SerializeValue(ref FinishTime);
            serializer.SerializeValue(ref Title);
        }

        public bool Equals(DataPacket other) =>
            Id == other.Id && Status == other.Status && FinishTime.Equals(other.FinishTime);

        public override bool Equals(object obj) => obj is DataPacket other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Id, (int)Status, FinishTime);
    }

    /// <summary>A batch of data the crew submitted, for the Balance app's history.</summary>
    public struct SubmissionRecord : INetworkSerializable, IEquatable<SubmissionRecord>
    {
        public int Count;
        public int Credits;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Count);
            serializer.SerializeValue(ref Credits);
        }

        public bool Equals(SubmissionRecord other) => Count == other.Count && Credits == other.Credits;
        public override bool Equals(object obj) => obj is SubmissionRecord other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Count, Credits);
    }
}
