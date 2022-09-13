namespace Arachne;

public interface ISerializable
{
    void Serialize(BinaryWriter writer);
    static abstract object Deserialize(BinaryReader reader);
}