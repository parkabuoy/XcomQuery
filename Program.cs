using Newtonsoft.Json;
using System.Data;
using XcomQuery;

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
foreach (CheckpointTable entity in (save.Checkpoints[0].Checkpoint_table ?? []).Where(x => x.Class_name == "XComStrategyGame.XGStrategySoldier"))
{
  if (entity.Properties is not null)
  {
    // get soldier/character property arrays
    // properties both contain values (name, etc) and lists of more properties
    Property soldierProp = entity.Properties.Where(x => x.Name == "m_kSoldier").First();
    Property charProp = entity.Properties.Where(x => x.Name == "m_kChar").First();

    if (soldierProp.Properties is not null)
    {
      // build soldier
      Soldier thisSoldier = new()
      {
        Id = (long)(soldierProp.Properties.First(x => x.Name == "iID").Value ?? -1),
        Perks = [],
        Stats = new(),
        // use helpers to make this a little cleaner
        LName = StringProp(soldierProp, "strLastName"),
        NName = StringProp(soldierProp, "strNickName"),
        FName = StringProp(soldierProp, "strFirstName"),
        Rank = LongProp(soldierProp, "iRank").GetValueOrDefault(),
        Xp = LongProp(soldierProp, "iXP").GetValueOrDefault(),
        // status is in the parent entity
        Status = ((entity.Properties.First(x => x.Name == "m_eStatus").Value ?? "").ToString() ?? "").TrimStart("eStatus_".ToCharArray())
      };

      // get the perks taken
      // these are stored as an array of integers in aUpgrades, 176 of them (one per perk)
      int[] aUpgrades = [];
      if ((charProp.Properties ?? []).Any(x => x.Name == "aUpgrades"))
      {
        // get the upgrades array and walk through it
        aUpgrades = [.. (charProp.Properties ?? []).First(x => x.Name == "aUpgrades").Int_values ?? []];
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
                thisSoldier.Perks.Add(new() { Id = i, Name = row["Name"].ToString() ?? "" });
                break;
              }
            }
          }
        }
      }

      int[] aStats = [];
      if ((charProp.Properties ?? []).Any(x => x.Name == "aStats"))
      {
        aStats = [.. (charProp.Properties ?? []).First(x => x.Name == "aStats").Int_values ?? []];
        for (int statIndex = 0; statIndex < aStats.Length; statIndex++)
        {
          switch (statIndex)
          {
            case 0:
              thisSoldier.Stats.HP = aStats[statIndex];
              break;
            case 1:
              thisSoldier.Stats.Aim = aStats[statIndex];
              break;
            case 2:
              thisSoldier.Stats.Defense = aStats[statIndex];
              break;
            case 3:
              thisSoldier.Stats.Mobility = aStats[statIndex];
              break;
            case 7:
              thisSoldier.Stats.Will = aStats[statIndex];
              break;
          }
        }
      }

      // add the soldier to the roster
      roster.Add(thisSoldier);
    }
  }
}

// output the results
OutputTSV();

void OutputTSV()
{
  string perkNames = "";
  string indicator = "●";

  // build a tab-separated string of all perk names by iterating through perk reference table
  for (int i = 0; i < perkRef.Rows.Count; i++) perkNames += $"\t{perkRef.Rows[i]["Name"]}";

  // write header row
  Show(string.Join("\t", ["id"
    ,"lName"
    ,"nName"
    ,"rank"
    ,"status"
    ,"MOB"
    ,"HP"
    ,"DEF"
    ,"WILL"
    ,"AIM"
    ,perkNames[1..]
  ]));

  // build soldier lines - iterate through roster
  foreach (Soldier thisSoldier in roster.Where(x => x.Status != "Dead" && !string.IsNullOrEmpty(x.LName)))
  {
    string perkFlags = "";

    // iterate through perk ref list and set this soldier's flag for each perk
    for (int i = 0; i < perkRef.Rows.Count; i++) perkFlags += "\t" + (thisSoldier.Perks.Any(x => x.Name == perkRef.Rows[i]["Name"].ToString()) ? indicator : "");

    // write the line for this soldier
    Show(string.Join("\t", [
      thisSoldier.Id
      ,thisSoldier.LName
      ,thisSoldier.NName
      ,thisSoldier.Rank
      ,thisSoldier.Status
      ,thisSoldier.Stats.Mobility
      ,thisSoldier.Stats.HP
      ,thisSoldier.Stats.Defense
      ,thisSoldier.Stats.Will
      ,thisSoldier.Stats.Aim
      ,perkFlags[1..]
    ]));
  }

  Console.WriteLine();
  Console.WriteLine("Save output to " + outputPath + "? y/n");
  if (Console.ReadKey().KeyChar == 'y') File.WriteAllText(outputPath, outputLedger);
}

string StringProp(Property prop, string name)
{
  foreach (Property stringProp in (prop.Properties ?? []).Where(x => x.Name == name))
  {
    return (JsonConvert.DeserializeObject<XValue>((stringProp.Value ?? "").ToString() ?? "") ?? new XValue()).Str ?? "";
  }

  return "";
}

long? LongProp(Property prop, string name)
{
  if ((prop.Properties ?? []).Exists(x => x.Name == name))
    return (long)(prop.Properties.First(x => x.Name == name).Value ?? "-1");
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
  using (StreamReader sr = new(strFilePath))
  {
    string[] headers = (sr.ReadLine() ?? "").Split(',');
    foreach (string header in headers)
    {
      dt.Columns.Add(header);
    }
    while (!sr.EndOfStream)
    {
      string[] rows = (sr.ReadLine() ?? "").Split(',');
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

