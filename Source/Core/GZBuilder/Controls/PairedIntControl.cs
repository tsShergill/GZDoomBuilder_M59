﻿#region ================== Namespaces

using System;
using System.Windows.Forms;
using CodeImp.DoomBuilder.Map;

#endregion

namespace CodeImp.DoomBuilder.GZBuilder.Controls
{
	public partial class PairedIntControl : UserControl
	{
		#region ================== Events

		public event EventHandler OnValuesChanged;

		#endregion

		#region ================== Variables

		private int defaultValue;
		private bool blockUpdate;

		#endregion

		#region ================== Properties

		public string Label { get { return label.Text; } set { label.Text = value; } }
		public int DefaultValue { get { return defaultValue; } set { defaultValue = value; } }
		public int ButtonStep { get { return (int)value1.ButtonStep; } set { value1.ButtonStep = value; value2.ButtonStep = value; } }

		#endregion

		public PairedIntControl() {
			InitializeComponent();
		}

		public void SetValues(int val1, int val2) {
			blockUpdate = true;

			if(!string.IsNullOrEmpty(value1.Text) && value1.Text != val1.ToString())
				value1.Text = "";
			else
				value1.Text = val1.ToString();

			if(!string.IsNullOrEmpty(value2.Text) && value2.Text != val2.ToString())
				value2.Text = "";
			else
				value2.Text = val2.ToString();

			blockUpdate = false;
		}

		public int GetValue1(int original) {
			return value1.GetResult(original);
		}

		public int GetValue2(int original) {
			return value2.GetResult(original);
		}

		private void checkValues() {
			bool changed = (string.IsNullOrEmpty(value1.Text) || string.IsNullOrEmpty(value2.Text));

			if(!changed)
				changed = (value1.GetResult(defaultValue) != defaultValue || value2.GetResult(defaultValue) != defaultValue);

			label.Enabled = changed;
			bReset.Visible = changed;

			if(!blockUpdate && OnValuesChanged != null) OnValuesChanged(this, EventArgs.Empty);
		}

		private void bReset_Click(object sender, EventArgs e) {
			value1.Text = defaultValue.ToString();
			value2.Text = defaultValue.ToString();
			checkValues();
		}

		private void value1_WhenTextChanged(object sender, EventArgs e) {
			checkValues();
		}
	}
}
