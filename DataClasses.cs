using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace XcomQuery
{
  //----------------------------------------------------------------------------- classes
  public class Soldier
  {
    public required string FName { get; set; }
    public required string NName { get; set; }
    public required string LName { get; set; }
    public required long Id { get; set; }
    public required long Rank { get; set; }
    public required long Xp { get; set; }
    public required string Status { get; set; }
    public required List<Perk> Perks { get; set; }
    public required SoldierStats Stats { get; set; }
  }

  public class SoldierStats
  {
    public long Mobility { get; set; }
    public long Defense { get; set; }
    public long Will { get; set; }
    public long HP { get; set; }
    public long Aim { get; set; }
  }

  public class Perk
  {
    public long Id { get; set; }
    public string? Name { get; set; }
  }

  public class Checkpoint
  {
    public int Unknown_int1 { get; set; }
    public string? Game_type { get; set; }
    public List<CheckpointTable>? Checkpoint_table { get; set; }
    public int Unknown_int2 { get; set; }
    public string? Class_name { get; set; }
    public List<string>? Actor_table { get; set; }
    public int Unknown_int3 { get; set; }
    public string? Display_name { get; set; }
    public string? Map_name { get; set; }
    public int Unknown_int4 { get; set; }
  }

  public class CheckpointTable
  {
    public string? Name { get; set; }
    public string? Instance_name { get; set; }
    public string? Class_name { get; set; }
    public List<double>? Vector { get; set; }
    public List<int>? Rotator { get; set; }
    public List<Property>? Properties { get; set; }
    public int Template_index { get; set; }
    public int Pad_size { get; set; }
  }

  public class EnumValue
  {
    public string? Value { get; set; }
    public int Number { get; set; }
  }

  public class Header
  {
    public int Version { get; set; }
    public int Uncompressed_size { get; set; }
    public int Game_number { get; set; }
    public int Save_number { get; set; }
    public XValue? Save_description { get; set; }
    public XValue? Time { get; set; }
    public string? Map_command { get; set; }
    public bool Tactical_save { get; set; }
    public bool Ironman { get; set; }
    public bool Autosave { get; set; }
    public string? Dlc { get; set; }
    public string? Language { get; set; }
  }

  public class Property
  {
    public string? Name { get; set; }
    public string? Kind { get; set; }
    public int Actor { get; set; }
    public object? Value { get; set; }
    public List<int>? Elements { get; set; }
    public List<int>? Actors { get; set; }
    public List<List<XStruct>>? structs { get; set; }
    public string? Struct_name { get; set; }
    public string? Native_data { get; set; }
    public List<Property>? Properties { get; set; }
    public int? Data_length { get; set; }
    public int? Array_bound { get; set; }
    public string? Data { get; set; }
    public List<XValue>? Enum_values { get; set; }
    public string? Type { get; set; }
    public int? Number { get; set; }
    public List<XValue>? Strings { get; set; }
    public List<int>? Int_values { get; set; }
  }

  public class JsonRoot
  {
    public required Header Header { get; set; }
    public required List<string> Actor_table { get; set; }
    public required List<Checkpoint> Checkpoints { get; set; }
  }

  public class XValue
  {
    public string? Str { get; set; }
    public bool Is_wide { get; set; }
  }

  public class XStruct
  {
    public string? Name { get; set; }
    public string? Kind { get; set; }
    public string? Struct_name { get; set; }
    public string? Native_data { get; set; }
    public List<Property>? properties { get; set; }
    public List<long>? Elements { get; set; }
    public required object Value { get; set; }
    public required List<List<XStruct>> Structs { get; set; }
  }

}
