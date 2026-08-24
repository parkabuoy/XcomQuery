using Newtonsoft.Json;
using System.Data;

//----------------------------------------------------------------------------- config

// set filename of save, output from xcom2json
string saveFilename = "save43.json";

// resulting tab-separated list is saved here
string outputPath = "..\\..\\..\\txt\\output.txt";

//----------------------------------------------------------------------------- parsing

// build datatable out of csv file (directly copied from swf's id reference sheets)
DataTable perkRef = ConvertCSVtoDataTable("..\\..\\..\\csv\\Long War ID reference - Perks.csv");

// parse save json into a class with newtonsoft.jsonconvert
// classes were generated from parsing various json nodes w/app.quicktype.io
JsonRoot? save = JsonConvert.DeserializeObject<JsonRoot>(File.ReadAllText($"..\\..\\..\\txt\\{saveFilename}"));

// if parsing failed, cry
if (save is null)
{
  Console.WriteLine($"json parsing failure{Environment.NewLine}{saveFilename}");
  Console.Read();
  Environment.Exit(0);
}

string outputLedger = "";
List<Soldier> roster = [];

//----------------------------------------------------------------------------- mapping
// in parsed json, step through the parts which relate to soldiers
foreach (CheckpointTable entity in save.checkpoints[0].checkpoint_table.Where(x => x.class_name == "XComStrategyGame.XGStrategySoldier"))
{
  if (entity.properties is not null)
  {
    // get soldier/character property arrays
    // properties both contain values (name, etc) and lists of more properties
    Property soldierProp = entity.properties.Where(x => x.name == "m_kSoldier").First();
    Property charProp = entity.properties.Where(x => x.name == "m_kChar").First();

    if (soldierProp.properties is not null)
    {
      // build soldier
      Soldier thisSoldier = new()
      {
        id = (long)soldierProp.properties.Where(x => x.name == "iID").First().value,
        perksTaken = [],
        stats = new(),
        // use helpers to make this a little cleaner
        lName = StringProp(soldierProp, "strLastName"),
        nName = StringProp(soldierProp, "strNickName"),
        fName = StringProp(soldierProp, "strFirstName"),
        rank = LongProp(soldierProp, "iRank").GetValueOrDefault(),
        xp = LongProp(soldierProp, "iXP").GetValueOrDefault(),
        // status is in the parent entity
        status = (entity.properties.First(x => x.name == "m_eStatus").value.ToString() ?? "").TrimStart("eStatus_".ToCharArray())
      };

      // get the perks taken
      // these are stored as an array of integers in aUpgrades, 176 of them (one per perk)
      int[] aUpgrades = [];
      if (charProp.properties.Any(x => x.name == "aUpgrades"))
      {
        // get the upgrades array and walk through it
        aUpgrades = [.. charProp.properties.First(x => x.name == "aUpgrades").int_values];
        for (int i = 0; i < aUpgrades.Length; i++)
        {
          // if the array value is 1, that perk was picked
          // if the array value is 2 or 3 that means it's from a medal or something
          if (aUpgrades[i] == 1)
          {
            // step through the perk reference sheet until we find the perk which matches this index in the aUpgrades array
            foreach (DataRow row in perkRef.Rows)
            {
              if (Int32.Parse(row["ID"].ToString() ?? "") == i)
              {
                thisSoldier.perksTaken.Add(new() { id = i, name = row["Name"].ToString() ?? "" });
                break;
              }
            }
          }
        }
      }

      int[] aStats = [];
      if (charProp.properties.Any(x => x.name == "aStats"))
      {
        aStats = [.. charProp.properties.First(x => x.name == "aStats").int_values];
        for (int i = 0; i < aStats.Length; i++)
        {
          switch (i)
          {
            case 0:
              thisSoldier.stats.HP = aStats[i];
              break;
            case 1:
              thisSoldier.stats.Aim = aStats[i];
              break;
            case 2:
              thisSoldier.stats.Defense = aStats[i];
              break;
            case 3:
              thisSoldier.stats.Mobility = aStats[i];
              break;
            case 7:
              thisSoldier.stats.Will = aStats[i];
              break;
          }
        }
      }

      // add the soldier to the roster
      roster.Add(thisSoldier);
    }
  }
}

//----------------------------------------------------------------------------- output
string perkNames = "";
string indicator = "●";

// build a tab-separated string of all perk names by iterating through perk reference table
for (int i = 0; i < perkRef.Rows.Count; i++) perkNames += $"\t{perkRef.Rows[i]["Name"]}";

// write header row
Show($"id\tlName\tnName\trank\tstatus\tMOB\tHP\tDEF\tWILL\tAIM{perkNames}");

// build soldier lines - iterate through roster
foreach (Soldier thisSoldier in roster.Where(x => x.status != "Dead" && !string.IsNullOrEmpty(x.lName)))
{
  string perkFlags = "";

  // iterate through perk ref list and set this soldier's flag for each perk
  for (int i = 0; i < perkRef.Rows.Count; i++)
  {
    // add tab for this position regardless
    perkFlags += "\t";

    // if they have this perk (if any of their perks taken match this perk name), add the indicator string
    if (thisSoldier.perksTaken.Any(x => x.name == perkRef.Rows[i]["Name"].ToString())) perkFlags += indicator;
  }

  // write the line for this soldier
  Show($"{thisSoldier.id}\t{thisSoldier.lName}\t{thisSoldier.nName}\t{thisSoldier.rank}\t{thisSoldier.status}" +
    $"\t{thisSoldier.stats.Mobility}\t{thisSoldier.stats.HP}\t{thisSoldier.stats.Defense}\t{thisSoldier.stats.Will}\t{thisSoldier.stats.Aim}" +
    $"{perkFlags}");
}

Console.WriteLine();
Console.WriteLine("Save output to " + outputPath + "? y/n");
if (Console.ReadKey().KeyChar == 'y') File.WriteAllText(outputPath, outputLedger);

//----------------------------------------------------------------------------- helpers
string StringProp(Property prop, string name)
{
  if (prop.properties.Exists(x => x.name == name))
    return JsonConvert.DeserializeObject<XValue>(prop.properties.First(x => x.name == name).value.ToString()).str;
  else return "";
}

long? LongProp(Property prop, string name)
{
  if (prop.properties.Exists(x => x.name == name))
    return (long)prop.properties.Where(x => x.name == name).First().value;
  else return null;
}

// output to console and global string so it can be saved to a file
void Show(string line)
{
  Console.WriteLine(line);
  outputLedger += line + Environment.NewLine;
}

// converts a csv to a data table :)
static DataTable ConvertCSVtoDataTable(string strFilePath)
{
  DataTable dt = new();
  using (StreamReader sr = new StreamReader(strFilePath))
  {
    string[] headers = sr.ReadLine().Split(',');
    foreach (string header in headers)
    {
      dt.Columns.Add(header);
    }
    while (!sr.EndOfStream)
    {
      string[] rows = sr.ReadLine().Split(',');
      DataRow dr = dt.NewRow();
      for (int i = 0; i < headers.Length; i++)
      {
        dr[i] = rows[i];
      }
      dt.Rows.Add(dr);
    }
  }
  return dt;
}

//----------------------------------------------------------------------------- classes
public class Soldier
{
  public string fName { get; set; }
  public string nName { get; set; }
  public string lName { get; set; }
  public long id { get; set; }
  public long rank { get; set; }
  public long xp { get; set; }
  public string status { get; set; }
  public List<Perk> perksTaken { get; set; }
  public SoldierStats stats {get;set;}
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
  public long id { get; set; }
  public string name { get; set; }
}

public class Checkpoint
{
  public int unknown_int1 { get; set; }
  public string game_type { get; set; }
  public List<CheckpointTable> checkpoint_table { get; set; }
  public int unknown_int2 { get; set; }
  public string class_name { get; set; }
  public List<string> actor_table { get; set; }
  public int unknown_int3 { get; set; }
  public string display_name { get; set; }
  public string map_name { get; set; }
  public int unknown_int4 { get; set; }
}

public class CheckpointTable
{
  public string name { get; set; }
  public string instance_name { get; set; }
  public string class_name { get; set; }
  public List<double> vector { get; set; }
  public List<int> rotator { get; set; }
  public List<Property> properties { get; set; }
  public int template_index { get; set; }
  public int pad_size { get; set; }
}

public class EnumValue
{
  public string value { get; set; }
  public int number { get; set; }
}

public class Header
{
  public int version { get; set; }
  public int uncompressed_size { get; set; }
  public int game_number { get; set; }
  public int save_number { get; set; }
  public XValue save_description { get; set; }
  public XValue time { get; set; }
  public string map_command { get; set; }
  public bool tactical_save { get; set; }
  public bool ironman { get; set; }
  public bool autosave { get; set; }
  public string dlc { get; set; }
  public string language { get; set; }
}

public class Property
{
  public string name { get; set; }
  public string kind { get; set; }
  public int actor { get; set; }
  public object value { get; set; }
  public List<int> elements { get; set; }
  public List<int> actors { get; set; }
  public List<List<XStruct>> structs { get; set; }
  public string struct_name { get; set; }
  public string native_data { get; set; }
  public List<Property> properties { get; set; }
  public int? data_length { get; set; }
  public int? array_bound { get; set; }
  public string data { get; set; }
  public List<XValue> enum_values { get; set; }
  public string type { get; set; }
  public int? number { get; set; }
  public List<XValue> strings { get; set; }
  public List<int> int_values { get; set; }
}

public class JsonRoot
{
  public Header header { get; set; }
  public List<string> actor_table { get; set; }
  public List<Checkpoint> checkpoints { get; set; }
}

public class XValue
{
  public string str { get; set; }
  public bool is_wide { get; set; }
}

public class XStruct
{
  public string name { get; set; }
  public string kind { get; set; }
  public string struct_name { get; set; }
  public string native_data { get; set; }
  public List<Property> properties { get; set; }
  public List<long> elements { get; set; }
  public object value { get; set; }
  public List<List<XStruct>> structs { get; set; }
}
