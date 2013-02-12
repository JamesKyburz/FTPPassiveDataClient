#FTPPassiveDataClient

A passive ftp client that is for people that hate files.

It only uses passive and it only uses in memory byte arrays, so is not
suitable for very large files.

No encyption, compressions support.

#Example usage
```c#
  using (var ftp = new Ftp("ftp://username:password@madeup.com")) {
    ftp.TimeoutInMilliseconds = 5000;
    ftp.SetTransferType(TransferType.Binary);
    ftp.Log = (s) => Console.WriteLine(s);
    ftp.ChangeDirectory("test");
    foreach (var file in ftp.FileList("*.*")) {
      Console.WriteLine("File={0}", file);
      System.IO.File.WriteAllBytes(@"C:\" + file, ftp.GetData(file));
    }
    ftp.CreateDirectory("test");
    ftp.ChangeDirectory("test");
    ftp.SendData(new byte[] { 65, 65, 65 }, "o.txt");
    ftp.DeleteFile("o.txt");
    ftp.ChangeDirectory("..");
    ftp.DeleteDirectory("test");
  }
```
###Nuget

``` nuget
install-package FTPPassiveDataClient
```
