using Newtonsoft.Json;
using System.Data;
using System.Text.RegularExpressions;
using System.Diagnostics;
using XcomQuery;

string execTime = $"{DateTime.Now:yyyyMMdd.HHmm.}";

// resulting tab-separated list is saved here
string outputDir = "..\\..\\..\\output\\";
string x2jPath = "..\\..\\..\\exe\\xcom2json.exe";
string backupDir = "..\\..\\..\\saveBackup\\";
string saveDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) +  "\\Documents\\My Games\\XCOM - Enemy Within\\XComGame\\SaveData";

int savesToBackup = 5;

string saveFilenameFull = "";
string saveFilename = "";
string jsonFilenameFull = "";
string saveNameRegex = "^save\\d{1,3}\\Z"; // starts with "save", has 1-3 numbers after it, then ends
string todayBackupDir = Path.Combine(backupDir, $"{DateTime.Now:yyyyMMdd}");
string todayOutputDir = Path.Combine(outputDir, $"{DateTime.Now:yyyyMMdd}");

FileInfo? saveForAnalysis = null;
bool pathIsDir = true;

Console.WriteLine("Save directory:");
Console.WriteLine(saveDir);
Console.Write("Use this dir? y/n: ");

// if not 'y', asks for a file/directory
if (Console.ReadKey().KeyChar != 'y')
{
  Console.WriteLine();
  Console.WriteLine("Input save dir/file:");
  saveDir = (Console.ReadLine() ?? "").Replace("\"", "");
  saveForAnalysis = new(saveDir);

  if (!saveForAnalysis.Exists)
  {
    Console.WriteLine($"dir/file not found:{Environment.NewLine}{saveForAnalysis}{Environment.NewLine}Press any key to exit...");
    Console.ReadKey();
    Environment.Exit(0);
  }

  pathIsDir = (saveForAnalysis.Attributes & FileAttributes.Directory) == FileAttributes.Directory;
}

// was pointed to a directory
if (pathIsDir)
{

  foreach (FileInfo saveFile in new DirectoryInfo(saveDir)
    .GetFiles()
    .Where(x => Regex.IsMatch(x.Name, saveNameRegex))
    .OrderByDescending(x => x.LastWriteTime)
    .Take(savesToBackup))
  {
    // pluck first one (most recent) for analysis
    saveForAnalysis ??= saveFile;
    // back files up
    if (!Directory.Exists(todayBackupDir)) Directory.CreateDirectory(todayBackupDir);
    saveFile.CopyTo(Path.Combine(todayBackupDir, $"{execTime}{saveFile.Name}"), overwrite: true);
  }
}
// was pointed to a file manually
else
{
  saveForAnalysis = new(saveDir);
  if (saveForAnalysis is null || !saveForAnalysis.Exists)
  {
    Console.WriteLine($"file not found:{Environment.NewLine}{saveFilenameFull}{Environment.NewLine}Press any key to exit...");
    Console.ReadKey();
    Environment.Exit(0);
  }

  // back file up
  if (!Directory.Exists(todayBackupDir)) Directory.CreateDirectory(todayBackupDir);
  saveForAnalysis.CopyTo(Path.Combine(todayBackupDir, $"{execTime}{saveForAnalysis.Name}"), overwrite: true);
}

saveFilenameFull = (saveForAnalysis ?? new("")).FullName;
saveFilename = (saveForAnalysis ?? new("")).Name;

// build full filename of output json

if (!Directory.Exists(todayOutputDir)) Directory.CreateDirectory(todayOutputDir);
jsonFilenameFull = Path.Combine(todayOutputDir, $"{execTime}{saveFilename}.json");

// run xcom2json exe on save file
Process.Start("cmd", $"/C {x2jPath} -o \"{jsonFilenameFull}\" \"{saveFilenameFull}\"").WaitForExit();

// parse save json into a class with newtonsoft.jsonconvert
// classes were generated from parsing various json nodes w/app.quicktype.io
JsonRoot? saveJson = JsonConvert.DeserializeObject<JsonRoot>(File.ReadAllText(jsonFilenameFull));

// if parsing failed, cry
if (saveJson is null)
{
  Console.WriteLine($"json parsing failure{Environment.NewLine}{saveFilenameFull}{Environment.NewLine}Press any key to exit...");
  Console.ReadKey();
  Environment.Exit(0);
}

// build datatable out of csv file (directly copied from swf's id reference sheets)
DataTable perkRef = ConvertCSVtoDataTable("..\\..\\..\\csv\\Long War ID reference - Perks.csv");

string outputLedger = "";
List<Soldier> roster = [];

//----------------------------------------------------------------------------- mapping
// in parsed json, step through the parts which relate to soldiers
foreach (CheckpointTable entity in (saveJson.Checkpoints[0].Checkpoint_table ?? []).Where(x => x.Class_name == "XComStrategyGame.XGStrategySoldier"))
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
          // if the array value is 0, they don't have that perk
          if (aUpgrades[i] > 0)
          {
            // step through the perk reference sheet until we find the perk which matches this index in the aUpgrades array
            foreach (DataRow row in perkRef.Rows)
            {
              if (Int32.Parse(row["ID"].ToString() ?? "") == i)
              {
                thisSoldier.Perks.Add(new()
                {
                  Id = i,
                  Name = row["Name"].ToString() ?? "",
                  // 1 is a chosen perk, 2 and 3 are something else apparently. medals?
                  Type = aUpgrades[i]
                });
                break;
              }
            }
          }
        }
      }

      // get stats
      int[] aStats = [];
      if ((charProp.Properties ?? []).Any(x => x.Name == "aStats"))
      {
        // int values stored in an array whose index corresponds to hp, will, etc
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
    for (int i = 0; i < perkRef.Rows.Count; i++)
    {
      perkFlags += "\t";
      foreach (Perk thisPerk in thisSoldier.Perks.Where(x => x.Id == Int64.Parse(perkRef.Rows[i]["ID"].ToString() ?? ""))) perkFlags += thisPerk.Type;
    }

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

  string fullOutputPath = Path.Combine(todayOutputDir, $"{execTime}{saveFilename}.tsv");

  File.WriteAllText(fullOutputPath, outputLedger);

  Console.WriteLine();
  Console.WriteLine($"Output saved to {fullOutputPath}{Environment.NewLine}Press any key to exit...");
  Console.ReadKey();
  Environment.Exit(0);

}

// deserialize a string property
string StringProp(Property prop, string name)
{
  foreach (Property stringProp in (prop.Properties ?? []).Where(x => x.Name == name))
  {
    return (JsonConvert.DeserializeObject<XValue>((stringProp.Value ?? "").ToString() ?? "") ?? new XValue()).Str ?? "";
  }

  return "";
}

// deserialize a long property
long? LongProp(Property prop, string name)
{
  if ((prop.Properties ?? []).Exists(x => x.Name == name))
    return (long)((prop.Properties ?? []).First(x => x.Name == name).Value ?? "-1");
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

