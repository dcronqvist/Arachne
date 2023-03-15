namespace Arachne;

public interface ISerializable
{
    void Serialize(BinaryWriter writer);
    static virtual object Deserialize(BinaryReader reader) { throw new NotImplementedException(); }
}