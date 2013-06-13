#define UseXmlFilesForPlugins

using System;
using System.IO;            // for StreamWriter
using System.Runtime.InteropServices;   // for Marshal
using Microsoft.Win32;      // for Registry
using System.Diagnostics;   // for ProcessStartInfo
using System.Text;          // for ASCIIEncoding
using ECInterfaces;                     // for IEncConverter

namespace SilEncConverters40
{
	/// <summary>
	/// The ExeEncConverter class implements the EncConverter interface and has common
	/// code for developing an EncConverter plug-in that supports an exe-based converter
	/// (e.g. ITrans.exe, UTrans.exe, etc) that take input data via StandardInput and 
	/// return the converted data via StandardOutput/StandardError
	/// </summary>
	public abstract class ExeEncConverter : EncConverter
	{
        #region Member Variable Definitions
        protected string  m_strWorkingDir;
        protected string  m_strImplType;
        protected string  m_strWorkingDirSuffix;
        #endregion Member Variable Definitions

        #region Public Interface
        public string WorkingDir
        {
            get { return m_strWorkingDir; }
            set { m_strWorkingDir = value; }
        }

        public virtual string ExeName
        {
            get { return null; }
        }

        public virtual string Arguments
        {
            get { return null; }
        }
        #endregion Public Interface

        #region Initialization
        public ExeEncConverter
            (
            string  strProgramID,           // e.g. SilEncConverters40.ITrans (usually "typeof(<classname>).FullName")
            string  strImplType,            // e.g. "ITrans" (cf. SIL.tec)
            ConvType conversionType,        // e.g. ConvType.Legacy_to_Unicode
            string  lhsEncodingID,          // e.g. "ITrans" (c.f. "SIL-IPA93-2001")
            string  rhsEncodingID,          // e.g. "UNICODE"
            Int32   lProcessType,           // e.g. ProcessTypeFlags.UnicodeEncodingConversion
            string  strWorkingDirSuffix     // e.g. @"\SIL\Indic\ITrans"
            )
            : base(strProgramID,strImplType)
		{
            m_strImplType = strImplType;
            m_eConversionType = conversionType;
            m_strLhsEncodingID = lhsEncodingID;
            RightEncodingID = rhsEncodingID;
            ProcessType = lProcessType;
            m_strWorkingDirSuffix = strWorkingDirSuffix;
		}

        public override void Initialize(string converterName, string converterSpec,
            ref string lhsEncodingID, ref string rhsEncodingID, ref ConvType conversionType, 
            ref Int32 processTypeFlags, Int32 codePageInput, Int32 codePageOutput, bool bAdding)
        {
            base.Initialize
                (
                converterName, 
                converterSpec, 
                ref m_strLhsEncodingID, // since we may have already set these in the ctor, use our stored value and check for differences later
                ref m_strRhsEncodingID, // ibid
                ref m_eConversionType,  // ibid
                ref m_lProcessType,     // ibid
                codePageInput, 
                codePageOutput, 
                bAdding 
                );

            // normally, the sub-classes can specify the encoding ID, but if it's different
            //  go with what the user gives us (unless it's null)
            if(     !String.IsNullOrEmpty(lhsEncodingID) 
                &&  (String.Compare(m_strLhsEncodingID,lhsEncodingID,true) != 0) )
            {
                m_strLhsEncodingID = lhsEncodingID;
            }

            if(     !String.IsNullOrEmpty(rhsEncodingID) 
                &&  (String.Compare(m_strRhsEncodingID,rhsEncodingID,true) != 0) )
            {
                m_strRhsEncodingID = rhsEncodingID;
            }

            if( ConversionType != conversionType )
                m_eConversionType = conversionType;

            ProcessType |= processTypeFlags;

#if UseXmlFilesForPlugins
            WorkingDir = Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles) + m_strWorkingDirSuffix;
#else
            RegistryKey key = Registry.LocalMachine.OpenSubKey(EncConverters.HKLM_CNVTRS_SUPPORTED);
            if( key != null )
            {
                key = key.OpenSubKey(m_strImplType);
                if( key != null )
                    WorkingDir = (string)key.GetValue(strExePathKey);
            }
#endif
        }
        #endregion Initialization

        #region Derived Class overrides
        // these methods can be overridden in the subclasses to modify the way the I/O happens
        public virtual void WriteToExeInputStream(string strInput, StreamWriter input)
        {
            input.WriteLine(strInput);
            input.Close();
        }

        public virtual string ReadFromExeOutputStream(StreamReader srOutput, StreamReader srError)
        {
            string strReturn = srOutput.ReadToEnd();

            if( strReturn == "" )
            {
                strReturn = srError.ReadToEnd();
                srError.Close();
            }

            return strReturn;
        }

        // allow the sub-classes the ability to override how the process is started.
        protected ProcessStartInfo m_psi = null;
        protected virtual ProcessStartInfo ProcessStarter
        {
            get
            {
                if( m_psi == null )
                {
                	var progpath = Path.Combine(WorkingDir, ExeName);
					if (!File.Exists(progpath))
						progpath = ExeName;		// assume program is in the user's path.

                    m_psi = new ProcessStartInfo(progpath)
                                {
                                    Arguments = Arguments,
                                    WorkingDirectory = WorkingDir,
                                    UseShellExecute = false,
                                    CreateNoWindow = true,
                                    RedirectStandardInput = true,
                                    RedirectStandardOutput = true,
                                    RedirectStandardError = true,
                                    StandardOutputEncoding = StandardOutputEncoding
                                };
                }
                return m_psi;
            }
        }

	    protected virtual Encoding StandardOutputEncoding
	    {
	        get
	        {
                return Encoding.UTF8;
	        }
	    }

        protected virtual Encoding StandardInputEncoding
        {
            get
            {
                return Encoding.UTF8;
            }
        }

        #endregion Derived Class overrides

        #region Implementation
        protected string DoExeCall(string sInput)
        {
            var si = ProcessStarter;
            string strOutput;

            try
            {
                var p = Process.Start(si);
                
                // set up the writer to use the correct code page
                var sw = (p.StandardInput.Encoding != StandardInputEncoding)
                             ? new StreamWriter(p.StandardInput.BaseStream, StandardInputEncoding)
                             : p.StandardInput;

                // call a virtual to do this in case the sub-classes have special behavior
                WriteToExeInputStream(sInput,sw);

                // set up the reader to use the correct code page
                var sr = p.StandardOutput;

                // call a virtual to do this in case the sub-classes have special behavior
                strOutput = ReadFromExeOutputStream(sr, p.StandardError);
            }
            catch(Exception e)
            {
                if (e.Message == "The system cannot find the file specified")
                    throw new ApplicationException(String.Format("Unable to find the file '{0}'. Is the proper program distribution installed?", si.FileName), e);
                throw;
            }

            return strOutput;
        }
        #endregion Implementation

        #region Abstract Base Class Overrides
        [CLSCompliant(false)]
        protected override unsafe void DoConvert
            (
            byte*       lpInBuffer,
            int         nInLen,
            byte*       lpOutBuffer,
            ref int     rnOutLen
            )
        {
            rnOutLen = 0;

            // we need to put it *back* into a string because the StreamWriter that will
            // ultimately write to the StandardInput uses a string. For now, the only user
            //  is Perl, which only supports Unicode to Unicode and so the data coming in
            //  will be UTF-16. So to put it back into a string, we just need to use this:
            var baDst = new byte [nInLen];
            ECNormalizeData.ByteStarToByteArr(lpInBuffer,nInLen,baDst);
            var enc = Encoding.Unicode;
            var strInput = enc.GetString(baDst);
                
            // call the helper that calls the exe
            var strOutput = DoExeCall(strInput);

#if DEBUG && __MonoCS__
			Console.Error.WriteLine("Got result from system call: " + strOutput);
            byte[] baOut2 = Encoding.Unicode.GetBytes(strOutput);  // easier to read
            dispBytes("Output UTF16LE", baOut2);

			TextWriter tw = new StreamWriter(
				Path.Combine(Path.GetTempPath(), "returning.txt"));
            tw.WriteLine("input: '"  + strInput + "'");
            tw.WriteLine("output: '" + strOutput + "'");
            tw.Close();
#endif

            // if there's a response...
            if (String.IsNullOrEmpty(strOutput))
                return;

            // put it in the output buffer 
            rnOutLen = strOutput.Length * 2;
            rnOutLen = ECNormalizeData.StringToByteStar(strOutput,lpOutBuffer,rnOutLen,false);
        }
        #endregion Abstract Base Class Overrides
    }
}
