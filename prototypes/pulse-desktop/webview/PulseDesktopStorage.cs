using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace CodexCompanion.PulseWebPreview {
  // 只读原数据库（包含已提交 WAL）；不拷贝凭据库，不打开可写连接，不读密码表。
  internal static class PulseDesktopStorage {
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)] static extern int sqlite3_open_v2(byte[] path,out IntPtr db,int flags,IntPtr vfs);
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)] static extern int sqlite3_close(IntPtr db);
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)] static extern int sqlite3_busy_timeout(IntPtr db,int ms);
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)] static extern int sqlite3_prepare_v2(IntPtr db,byte[] sql,int length,out IntPtr statement,IntPtr tail);
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)] static extern int sqlite3_step(IntPtr statement);
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)] static extern int sqlite3_finalize(IntPtr statement);
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)] static extern int sqlite3_column_count(IntPtr statement);
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)] static extern int sqlite3_column_bytes(IntPtr statement,int column);
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)] static extern IntPtr sqlite3_column_blob(IntPtr statement,int column);
    internal static List<byte[][]> Query(string path,string sql) {
      var rows=new List<byte[][]>();if(!File.Exists(path))return rows;
      IntPtr db=IntPtr.Zero,statement=IntPtr.Zero;
      try {
        if(sqlite3_open_v2(Encoding.UTF8.GetBytes(path+"\0"),out db,1,IntPtr.Zero)!=0)throw new PulseAccountException("local-storage");
        sqlite3_busy_timeout(db,300);
        if(sqlite3_prepare_v2(db,Encoding.UTF8.GetBytes(sql+"\0"),-1,out statement,IntPtr.Zero)!=0)throw new PulseAccountException("local-storage");
        int status;
        while((status=sqlite3_step(statement))==100) {
          if(rows.Count>=16)throw new PulseAccountException("local-storage");
          var row=new byte[sqlite3_column_count(statement)][];
          for(int column=0;column<row.Length;column++) {
            int size=sqlite3_column_bytes(statement,column);if(size>65536)throw new PulseAccountException("local-storage");
            row[column]=new byte[size];if(size>0)Marshal.Copy(sqlite3_column_blob(statement,column),row[column],0,size);
          }
          rows.Add(row);
        }
        if(status!=101)throw new PulseAccountException("local-storage");
        return rows;
      } finally {if(statement!=IntPtr.Zero)sqlite3_finalize(statement);if(db!=IntPtr.Zero)sqlite3_close(db);}
    }
    internal static string Text(byte[] raw) {return raw.Length>=2&&raw.Length%2==0&&raw[1]==0?Encoding.Unicode.GetString(raw):Encoding.UTF8.GetString(raw);}
    internal static object Json(string path,int maximum=1048576) {
      if(!File.Exists(path))return null;
      using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete)) {
        if(stream.Length>maximum)throw new PulseAccountException("local-storage");
        using(var reader=new StreamReader(stream,Encoding.UTF8))return new JavaScriptSerializer{MaxJsonLength=maximum}.DeserializeObject(reader.ReadToEnd());
      }
    }
    internal static byte[] Key(string profile) {
      string encoded=PulseUsageParser.Text(PulseUsageParser.Get(Json(Path.Combine(profile,"Local State")),"os_crypt"),"encrypted_key");
      if(encoded==null)throw new PulseAccountException("local-storage");
      var blob=Convert.FromBase64String(encoded);
      if(blob.Length<6||Encoding.ASCII.GetString(blob,0,5)!="DPAPI")throw new PulseAccountException("local-storage");
      return ProtectedData.Unprotect(blob.Skip(5).ToArray(),null,DataProtectionScope.CurrentUser);
    }
    // Windows 系统 CNG 校验 GCM tag；不手写密码算法。未知/app-bound 版本直接拒绝。
    internal static string Decrypt(byte[] blob,byte[] key,string host=null,int cookieVersion=0) {
      if(blob.Length<31||Encoding.ASCII.GetString(blob,0,3)!="v10")throw new PulseAccountException("local-storage");
      byte[] raw=PulseWindowsGcm.Open(key,blob.Skip(3).Take(12).ToArray(),blob.Skip(15).Take(blob.Length-31).ToArray(),blob.Skip(blob.Length-16).ToArray());
      try {
        int offset=0;
        if(host!=null&&cookieVersion>=24) {
          byte[] hash;using(var sha=SHA256.Create())hash=sha.ComputeHash(Encoding.UTF8.GetBytes(host));
          if(raw.Length<32||!hash.SequenceEqual(raw.Take(32)))throw new PulseAccountException("local-storage");
          offset=32;
        }
        return Encoding.UTF8.GetString(raw,offset,raw.Length-offset);
      } finally {Array.Clear(raw,0,raw.Length);}
    }
  }

  internal static class PulseWindowsGcm {
    [StructLayout(LayoutKind.Sequential)] struct AuthInfo {
      internal int size,version;internal IntPtr nonce;internal int nonceSize;internal IntPtr aad;internal int aadSize;
      internal IntPtr tag;internal int tagSize;internal IntPtr mac;internal int macSize,aadCount;internal long dataCount;internal int flags;
    }
    [DllImport("bcrypt.dll",CharSet=CharSet.Unicode)] static extern int BCryptOpenAlgorithmProvider(out IntPtr algorithm,string name,string implementation,int flags);
    [DllImport("bcrypt.dll",CharSet=CharSet.Unicode)] static extern int BCryptSetProperty(IntPtr handle,string property,byte[] input,int size,int flags);
    [DllImport("bcrypt.dll")] static extern int BCryptGenerateSymmetricKey(IntPtr algorithm,out IntPtr key,IntPtr keyObject,int objectSize,byte[] secret,int secretSize,int flags);
    [DllImport("bcrypt.dll")] static extern int BCryptDecrypt(IntPtr key,byte[] input,int size,ref AuthInfo auth,IntPtr iv,int ivSize,byte[] output,int outputSize,out int result,int flags);
    [DllImport("bcrypt.dll")] static extern int BCryptDestroyKey(IntPtr key);
    [DllImport("bcrypt.dll")] static extern int BCryptCloseAlgorithmProvider(IntPtr algorithm,int flags);
    internal static byte[] Open(byte[] secret,byte[] nonce,byte[] ciphertext,byte[] tag) {
      if(secret.Length!=32||nonce.Length!=12||tag.Length!=16)throw new PulseAccountException("local-storage");
      IntPtr algorithm=IntPtr.Zero,key=IntPtr.Zero;GCHandle n=default(GCHandle),t=default(GCHandle);
      byte[] plain=new byte[ciphertext.Length];
      try {
        if(BCryptOpenAlgorithmProvider(out algorithm,"AES",null,0)!=0)throw new PulseAccountException("local-storage");
        byte[] mode=Encoding.Unicode.GetBytes("ChainingModeGCM\0");
        if(BCryptSetProperty(algorithm,"ChainingMode",mode,mode.Length,0)!=0||BCryptGenerateSymmetricKey(algorithm,out key,IntPtr.Zero,0,secret,secret.Length,0)!=0)throw new PulseAccountException("local-storage");
        n=GCHandle.Alloc(nonce,GCHandleType.Pinned);t=GCHandle.Alloc(tag,GCHandleType.Pinned);
        var auth=new AuthInfo{size=Marshal.SizeOf(typeof(AuthInfo)),version=1,nonce=n.AddrOfPinnedObject(),nonceSize=nonce.Length,tag=t.AddrOfPinnedObject(),tagSize=tag.Length};
        int count;if(BCryptDecrypt(key,ciphertext,ciphertext.Length,ref auth,IntPtr.Zero,0,plain,plain.Length,out count,0)!=0||count!=plain.Length)throw new PulseAccountException("local-storage");
        return plain;
      } catch {Array.Clear(plain,0,plain.Length);throw;}
      finally {if(n.IsAllocated)n.Free();if(t.IsAllocated)t.Free();if(key!=IntPtr.Zero)BCryptDestroyKey(key);if(algorithm!=IntPtr.Zero)BCryptCloseAlgorithmProvider(algorithm,0);}
    }
  }
}
