using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace TINYLib
{
	[ComImport]
	[CompilerGenerated]
	[InterfaceType(2)]
	[Guid("1262A1CC-7B5C-4A3F-BFFB-4128027A0979")]
	[TypeIdentifier]
	public interface _DTiny
	{
		[DispId(1)]
		short TinyErrCode
		{
			[MethodImpl(MethodImplOptions.InternalCall)]
			[DispId(1)]
			get;
			[MethodImpl(MethodImplOptions.InternalCall)]
			[DispId(1)]
			set;
		}

		[DispId(2)]
		bool Initialize
		{
			[MethodImpl(MethodImplOptions.InternalCall)]
			[DispId(2)]
			get;
			[MethodImpl(MethodImplOptions.InternalCall)]
			[DispId(2)]
			set;
		}

		[DispId(3)]
		string UserPassWord
		{
			[MethodImpl(MethodImplOptions.InternalCall)]
			[DispId(3)]
			get;
			[MethodImpl(MethodImplOptions.InternalCall)]
			[DispId(3)]
			set;
		}

		[DispId(4)]
		bool ShowTinyInfo
		{
			[MethodImpl(MethodImplOptions.InternalCall)]
			[DispId(4)]
			get;
			[MethodImpl(MethodImplOptions.InternalCall)]
			[DispId(4)]
			set;
		}

		[DispId(6)]
		string SpecialID
		{
			[MethodImpl(MethodImplOptions.InternalCall)]
			[DispId(6)]
			get;
			[MethodImpl(MethodImplOptions.InternalCall)]
			[DispId(6)]
			set;
		}

		[DispId(7)]
		string DataPartition
		{
			[MethodImpl(MethodImplOptions.InternalCall)]
			[DispId(7)]
			get;
			[MethodImpl(MethodImplOptions.InternalCall)]
			[DispId(7)]
			set;
		}

		[DispId(8)]
		int Counter
		{
			[MethodImpl(MethodImplOptions.InternalCall)]
			[DispId(8)]
			get;
			[MethodImpl(MethodImplOptions.InternalCall)]
			[DispId(8)]
			set;
		}

		[DispId(11)]
		bool NetWorkINIT
		{
			[MethodImpl(MethodImplOptions.InternalCall)]
			[DispId(11)]
			get;
			[MethodImpl(MethodImplOptions.InternalCall)]
			[DispId(11)]
			set;
		}

		[DispId(10)]
		string ServerIP
		{
			[MethodImpl(MethodImplOptions.InternalCall)]
			[DispId(10)]
			get;
			[MethodImpl(MethodImplOptions.InternalCall)]
			[DispId(10)]
			set;
		}

		void _VtblGap1_2();

		void _VtblGap2_2();

		void _VtblGap3_4();
	}
}
