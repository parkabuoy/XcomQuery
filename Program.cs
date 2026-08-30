using Newtonsoft.Json;
using System.Data;
using System.Text.RegularExpressions;
using System.Diagnostics;
using XcomQuery;
using System.IO.Hashing;

// --------------------------------------------------------------------------------------------------------------------------------------------- DA CONFIG ZONE
string outputDir = "..\\..\\..\\output\\"; // the output dir
string backupDir = "..\\..\\..\\saveBackup\\"; // path where saves will be backed up
string x2jPath = "..\\..\\..\\exe\\xcom2json.exe"; // the path to xcom2json.exe, CRC 

// your xcom save directory
string savePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) +  "\\Documents\\My Games\\XCOM - Enemy Within\\XComGame\\SaveData";

// how many of the most recent saves will be backed up on running
int savesToBackup = 5;
int x2jHash= 27252167; // ensure the expected x2js version
// ------------------------------------------------------------------------------------------------------------------------------------------------------------

string saveFilenameFull = "";
string saveFilename = "";
string jsonFilenameFull = "";
string saveNameRegex = "^save\\d{1,3}\\Z"; // regex: starts with "save", has 1-3 numbers after it, then ends
string todayBackupDir = Path.Combine(backupDir, $"{DateTime.Now:yyyyMMdd}");
string todayOutputDir = Path.Combine(outputDir, $"{DateTime.Now:yyyyMMdd}");

string execTime = $"{DateTime.Now:yyyyMMdd.HHmm}";
FileInfo saveForAnalysis = null;

FileInfo x2jFile = new(x2jPath);
if (!x2jFile.Exists || x2jFile.GetHashCode() != x2jHash)
{
  Console.WriteLine($"invalid xcom2json exe!");
  Console.WriteLine(x2jFile.FullName);
  Console.WriteLine("Press any key to exit...");
  Console.ReadKey();
  Environment.Exit(0);

}

Console.WriteLine("Save directory:");
Console.WriteLine(savePath);
Console.Write("Use this dir? y/n: ");

// if not 'y', asks for a file/directory
if (Console.ReadKey().KeyChar != 'y')
{
  Console.WriteLine();
  Console.WriteLine("Input save dir/file:");
  savePath = (Console.ReadLine() ?? "").Replace("\"", "");
}

if (Directory.Exists(savePath))
{
  foreach (FileInfo saveFile in new DirectoryInfo(savePath)
    .GetFiles()
    .Where(x => Regex.IsMatch(x.Name, saveNameRegex)) // match "save[123]" regex
    .OrderByDescending(x => x.LastWriteTime) // get the most recent files
    .Take(savesToBackup)) // only a few
  {
    saveForAnalysis ??= saveFile; // pluck first one (most recent) for analysis (assign to it if it's null)
    BackupFile(saveFile); // back files up
  }
}
else if (File.Exists(savePath))
{
  saveForAnalysis = new(savePath);
  BackupFile(saveForAnalysis);
}
else
{
  Console.WriteLine($"dir/file not found:");
  Console.WriteLine(savePath);
  Console.WriteLine("Press any key to exit...");
  Console.ReadKey();
  Environment.Exit(0);
}

saveFilenameFull = (saveForAnalysis ?? new("")).FullName;
saveFilename = (saveForAnalysis ?? new("")).Name;

// build full filename of output json

if (!Directory.Exists(todayOutputDir)) Directory.CreateDirectory(todayOutputDir);
jsonFilenameFull = Path.Combine(todayOutputDir, $"{execTime}.{saveFilename}.json");

// run xcom2json exe on save file
Process.Start("cmd", $"/C {x2jPath} -o \"{jsonFilenameFull}\" \"{saveFilenameFull}\"").WaitForExit();

// if parsing failed, cry
if (!File.Exists(jsonFilenameFull))
{
  Console.WriteLine($"json parsing failure!");
  Console.WriteLine(saveFilenameFull);
  Console.WriteLine("Press any key to exit...");
  Console.ReadKey();
  Environment.Exit(0);
}

// parse save json into a class with newtonsoft.jsonconvert
// classes were generated from parsing various json nodes w/app.quicktype.io
JsonRoot? saveJson = JsonConvert.DeserializeObject<JsonRoot>(File.ReadAllText(jsonFilenameFull));

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

string fullOutputPath = Path.Combine(todayOutputDir, $"{execTime}.{saveFilename}.tsv");

// output the results
OutputTSV(fullOutputPath);

Console.WriteLine();
Console.WriteLine($"Output saved to: ");
Console.WriteLine(fullOutputPath);
Console.WriteLine("Press any key to exit...");
Console.ReadKey();
Environment.Exit(0);

// back files up
void BackupFile(FileInfo saveFile)
{
  if (!Directory.Exists(todayBackupDir)) Directory.CreateDirectory(todayBackupDir);
  saveFile.CopyTo(Path.Combine(todayBackupDir, $"{execTime}.{saveFile.Name}"), overwrite: true);
}
void OutputTSV(string outPath)
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

    // iterate through perk ref list and set this soldier's value for each perk
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

  // write file out
  File.WriteAllText(outPath, outputLedger);
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
