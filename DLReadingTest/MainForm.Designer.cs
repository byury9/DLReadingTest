namespace DLReadingTest
{
    partial class MainForm
    {
        /// <summary>
        /// 필수 디자이너 변수입니다.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// 사용 중인 모든 리소스를 정리합니다.
        /// </summary>
        /// <param name="disposing">관리되는 리소스를 삭제해야 하면 true이고, 그렇지 않으면 false입니다.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form 디자이너에서 생성한 코드

        /// <summary>
        /// 디자이너 지원에 필요한 메서드입니다.
        /// 이 메서드의 내용을 코드 편집기로 수정하지 마세요.
        /// </summary>
        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle1 = new System.Windows.Forms.DataGridViewCellStyle();
            this.flowTop = new System.Windows.Forms.FlowLayoutPanel();
            this.btnOpenFolder = new System.Windows.Forms.Button();
            this.btnOpenModel = new System.Windows.Forms.Button();
            this.sep1 = new System.Windows.Forms.Label();
            this.btnPrev = new System.Windows.Forms.Button();
            this.btnPlay = new System.Windows.Forms.Button();
            this.btnNext = new System.Windows.Forms.Button();
            this.lblInterval = new System.Windows.Forms.Label();
            this.numInterval = new System.Windows.Forms.NumericUpDown();
            this.sep2 = new System.Windows.Forms.Label();
            this.chkRoiEdit = new System.Windows.Forms.CheckBox();
            this.btnClearRoi = new System.Windows.Forms.Button();
            this.sep3 = new System.Windows.Forms.Label();
            this.btnRead = new System.Windows.Forms.Button();
            this.chkAutoRead = new System.Windows.Forms.CheckBox();
            this.lblConf = new System.Windows.Forms.Label();
            this.numConf = new System.Windows.Forms.NumericUpDown();
            this.lblExpected = new System.Windows.Forms.Label();
            this.txtExpected = new System.Windows.Forms.TextBox();
            this.btnVerify = new System.Windows.Forms.Button();
            this.split = new System.Windows.Forms.SplitContainer();
            this.viewer = new DLReadingTest.Controls.ImageViewer();
            this.gridResult = new System.Windows.Forms.DataGridView();
            this.colIndex = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.colLine = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.colChar = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.colConf = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.colX = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.colY = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.colW = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.colH = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.colJudge = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.colNote = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.grpResult = new System.Windows.Forms.GroupBox();
            this.txtResult = new System.Windows.Forms.TextBox();
            this.statusStrip = new System.Windows.Forms.StatusStrip();
            this.lblStatusFile = new System.Windows.Forms.ToolStripStatusLabel();
            this.lblStatusSize = new System.Windows.Forms.ToolStripStatusLabel();
            this.lblStatusRoi = new System.Windows.Forms.ToolStripStatusLabel();
            this.lblStatusTime = new System.Windows.Forms.ToolStripStatusLabel();
            this.lblStatusMouse = new System.Windows.Forms.ToolStripStatusLabel();
            this.lblStatusModel = new System.Windows.Forms.ToolStripStatusLabel();
            this.timerRotate = new System.Windows.Forms.Timer(this.components);
            this.flowTop.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.numInterval)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numConf)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.split)).BeginInit();
            this.split.Panel1.SuspendLayout();
            this.split.Panel2.SuspendLayout();
            this.split.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.gridResult)).BeginInit();
            this.grpResult.SuspendLayout();
            this.statusStrip.SuspendLayout();
            this.SuspendLayout();
            //
            // flowTop
            //
            this.flowTop.AutoSize = true;
            this.flowTop.Controls.Add(this.btnOpenFolder);
            this.flowTop.Controls.Add(this.btnOpenModel);
            this.flowTop.Controls.Add(this.sep1);
            this.flowTop.Controls.Add(this.btnPrev);
            this.flowTop.Controls.Add(this.btnPlay);
            this.flowTop.Controls.Add(this.btnNext);
            this.flowTop.Controls.Add(this.lblInterval);
            this.flowTop.Controls.Add(this.numInterval);
            this.flowTop.Controls.Add(this.sep2);
            this.flowTop.Controls.Add(this.chkRoiEdit);
            this.flowTop.Controls.Add(this.btnClearRoi);
            this.flowTop.Controls.Add(this.sep3);
            this.flowTop.Controls.Add(this.btnRead);
            this.flowTop.Controls.Add(this.chkAutoRead);
            this.flowTop.Controls.Add(this.lblConf);
            this.flowTop.Controls.Add(this.numConf);
            this.flowTop.Controls.Add(this.lblExpected);
            this.flowTop.Controls.Add(this.txtExpected);
            this.flowTop.Controls.Add(this.btnVerify);
            this.flowTop.Dock = System.Windows.Forms.DockStyle.Top;
            this.flowTop.Location = new System.Drawing.Point(0, 0);
            this.flowTop.Name = "flowTop";
            this.flowTop.Padding = new System.Windows.Forms.Padding(4);
            this.flowTop.Size = new System.Drawing.Size(1264, 76);
            this.flowTop.TabIndex = 0;
            //
            // btnOpenFolder
            //
            this.btnOpenFolder.Location = new System.Drawing.Point(7, 7);
            this.btnOpenFolder.Name = "btnOpenFolder";
            this.btnOpenFolder.Size = new System.Drawing.Size(90, 28);
            this.btnOpenFolder.TabIndex = 0;
            this.btnOpenFolder.Text = "폴더 열기";
            this.btnOpenFolder.UseVisualStyleBackColor = true;
            this.btnOpenFolder.Click += new System.EventHandler(this.btnOpenFolder_Click);
            //
            // btnOpenModel
            //
            this.btnOpenModel.Location = new System.Drawing.Point(103, 7);
            this.btnOpenModel.Name = "btnOpenModel";
            this.btnOpenModel.Size = new System.Drawing.Size(90, 28);
            this.btnOpenModel.TabIndex = 1;
            this.btnOpenModel.Text = "모델 열기";
            this.btnOpenModel.UseVisualStyleBackColor = true;
            this.btnOpenModel.Click += new System.EventHandler(this.btnOpenModel_Click);
            //
            // sep1
            //
            this.sep1.BorderStyle = System.Windows.Forms.BorderStyle.Fixed3D;
            this.sep1.Location = new System.Drawing.Point(202, 7);
            this.sep1.Margin = new System.Windows.Forms.Padding(6, 3, 6, 3);
            this.sep1.Name = "sep1";
            this.sep1.Size = new System.Drawing.Size(2, 28);
            this.sep1.TabIndex = 2;
            //
            // btnPrev
            //
            this.btnPrev.Location = new System.Drawing.Point(216, 7);
            this.btnPrev.Name = "btnPrev";
            this.btnPrev.Size = new System.Drawing.Size(70, 28);
            this.btnPrev.TabIndex = 3;
            this.btnPrev.Text = "◀ 이전";
            this.btnPrev.UseVisualStyleBackColor = true;
            this.btnPrev.Click += new System.EventHandler(this.btnPrev_Click);
            //
            // btnPlay
            //
            this.btnPlay.Location = new System.Drawing.Point(292, 7);
            this.btnPlay.Name = "btnPlay";
            this.btnPlay.Size = new System.Drawing.Size(90, 28);
            this.btnPlay.TabIndex = 4;
            this.btnPlay.Text = "▶ 재생";
            this.btnPlay.UseVisualStyleBackColor = true;
            this.btnPlay.Click += new System.EventHandler(this.btnPlay_Click);
            //
            // btnNext
            //
            this.btnNext.Location = new System.Drawing.Point(388, 7);
            this.btnNext.Name = "btnNext";
            this.btnNext.Size = new System.Drawing.Size(70, 28);
            this.btnNext.TabIndex = 5;
            this.btnNext.Text = "다음 ▶";
            this.btnNext.UseVisualStyleBackColor = true;
            this.btnNext.Click += new System.EventHandler(this.btnNext_Click);
            //
            // lblInterval
            //
            this.lblInterval.Location = new System.Drawing.Point(464, 7);
            this.lblInterval.Name = "lblInterval";
            this.lblInterval.Size = new System.Drawing.Size(60, 28);
            this.lblInterval.TabIndex = 6;
            this.lblInterval.Text = "간격(ms)";
            this.lblInterval.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            //
            // numInterval
            //
            this.numInterval.Increment = new decimal(new int[] { 100, 0, 0, 0 });
            this.numInterval.Location = new System.Drawing.Point(530, 10);
            this.numInterval.Margin = new System.Windows.Forms.Padding(3, 6, 3, 3);
            this.numInterval.Maximum = new decimal(new int[] { 60000, 0, 0, 0 });
            this.numInterval.Minimum = new decimal(new int[] { 100, 0, 0, 0 });
            this.numInterval.Name = "numInterval";
            this.numInterval.Size = new System.Drawing.Size(70, 21);
            this.numInterval.TabIndex = 7;
            this.numInterval.Value = new decimal(new int[] { 1000, 0, 0, 0 });
            this.numInterval.ValueChanged += new System.EventHandler(this.numInterval_ValueChanged);
            //
            // sep2
            //
            this.sep2.BorderStyle = System.Windows.Forms.BorderStyle.Fixed3D;
            this.sep2.Location = new System.Drawing.Point(609, 7);
            this.sep2.Margin = new System.Windows.Forms.Padding(6, 3, 6, 3);
            this.sep2.Name = "sep2";
            this.sep2.Size = new System.Drawing.Size(2, 28);
            this.sep2.TabIndex = 8;
            //
            // chkRoiEdit
            //
            this.chkRoiEdit.Appearance = System.Windows.Forms.Appearance.Button;
            this.chkRoiEdit.Location = new System.Drawing.Point(623, 7);
            this.chkRoiEdit.Name = "chkRoiEdit";
            this.chkRoiEdit.Size = new System.Drawing.Size(80, 28);
            this.chkRoiEdit.TabIndex = 9;
            this.chkRoiEdit.Text = "ROI 설정";
            this.chkRoiEdit.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            this.chkRoiEdit.UseVisualStyleBackColor = true;
            this.chkRoiEdit.CheckedChanged += new System.EventHandler(this.chkRoiEdit_CheckedChanged);
            //
            // btnClearRoi
            //
            this.btnClearRoi.Location = new System.Drawing.Point(709, 7);
            this.btnClearRoi.Name = "btnClearRoi";
            this.btnClearRoi.Size = new System.Drawing.Size(80, 28);
            this.btnClearRoi.TabIndex = 10;
            this.btnClearRoi.Text = "ROI 해제";
            this.btnClearRoi.UseVisualStyleBackColor = true;
            this.btnClearRoi.Click += new System.EventHandler(this.btnClearRoi_Click);
            //
            // sep3
            //
            this.sep3.BorderStyle = System.Windows.Forms.BorderStyle.Fixed3D;
            this.sep3.Location = new System.Drawing.Point(798, 7);
            this.sep3.Margin = new System.Windows.Forms.Padding(6, 3, 6, 3);
            this.sep3.Name = "sep3";
            this.sep3.Size = new System.Drawing.Size(2, 28);
            this.sep3.TabIndex = 11;
            //
            // btnRead
            //
            this.btnRead.Font = new System.Drawing.Font("맑은 고딕", 9F, System.Drawing.FontStyle.Bold);
            this.btnRead.Location = new System.Drawing.Point(812, 7);
            this.btnRead.Name = "btnRead";
            this.btnRead.Size = new System.Drawing.Size(80, 28);
            this.btnRead.TabIndex = 12;
            this.btnRead.Text = "읽기 (F5)";
            this.btnRead.UseVisualStyleBackColor = true;
            this.btnRead.Click += new System.EventHandler(this.btnRead_Click);
            //
            // chkAutoRead
            //
            this.chkAutoRead.Checked = true;
            this.chkAutoRead.CheckState = System.Windows.Forms.CheckState.Checked;
            this.chkAutoRead.Location = new System.Drawing.Point(898, 7);
            this.chkAutoRead.Name = "chkAutoRead";
            this.chkAutoRead.Size = new System.Drawing.Size(80, 28);
            this.chkAutoRead.TabIndex = 13;
            this.chkAutoRead.Text = "자동 읽기";
            this.chkAutoRead.UseVisualStyleBackColor = true;
            //
            // lblConf
            //
            this.lblConf.Location = new System.Drawing.Point(984, 7);
            this.lblConf.Name = "lblConf";
            this.lblConf.Size = new System.Drawing.Size(50, 28);
            this.lblConf.TabIndex = 14;
            this.lblConf.Text = "신뢰도";
            this.lblConf.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            //
            // numConf
            //
            this.numConf.DecimalPlaces = 2;
            this.numConf.Increment = new decimal(new int[] { 5, 0, 0, 131072 });
            this.numConf.Location = new System.Drawing.Point(1040, 10);
            this.numConf.Margin = new System.Windows.Forms.Padding(3, 6, 3, 3);
            this.numConf.Maximum = new decimal(new int[] { 99, 0, 0, 131072 });
            this.numConf.Minimum = new decimal(new int[] { 1, 0, 0, 131072 });
            this.numConf.Name = "numConf";
            this.numConf.Size = new System.Drawing.Size(60, 21);
            this.numConf.TabIndex = 15;
            this.numConf.Value = new decimal(new int[] { 25, 0, 0, 131072 });
            this.flowTop.SetFlowBreak(this.numConf, true);
            //
            // lblExpected
            //
            this.lblExpected.Location = new System.Drawing.Point(7, 41);
            this.lblExpected.Name = "lblExpected";
            this.lblExpected.Size = new System.Drawing.Size(84, 28);
            this.lblExpected.TabIndex = 16;
            this.lblExpected.Text = "검증할 문자";
            this.lblExpected.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            //
            // txtExpected
            //
            this.txtExpected.Font = new System.Drawing.Font("Consolas", 11F);
            this.txtExpected.Location = new System.Drawing.Point(97, 43);
            this.txtExpected.Margin = new System.Windows.Forms.Padding(3, 5, 3, 3);
            this.txtExpected.Name = "txtExpected";
            this.txtExpected.Size = new System.Drawing.Size(300, 25);
            this.txtExpected.TabIndex = 17;
            //
            // btnVerify
            //
            this.btnVerify.Font = new System.Drawing.Font("맑은 고딕", 9F, System.Drawing.FontStyle.Bold);
            this.btnVerify.Location = new System.Drawing.Point(403, 41);
            this.btnVerify.Name = "btnVerify";
            this.btnVerify.Size = new System.Drawing.Size(80, 28);
            this.btnVerify.TabIndex = 18;
            this.btnVerify.Text = "검증 (F6)";
            this.btnVerify.UseVisualStyleBackColor = true;
            this.btnVerify.Click += new System.EventHandler(this.btnVerify_Click);
            //
            // split
            //
            this.split.Dock = System.Windows.Forms.DockStyle.Fill;
            this.split.FixedPanel = System.Windows.Forms.FixedPanel.Panel2;
            this.split.Location = new System.Drawing.Point(0, 76);
            this.split.Name = "split";
            //
            // split.Panel1
            //
            this.split.Panel1.Controls.Add(this.viewer);
            //
            // split.Panel2
            //
            this.split.Panel2.Controls.Add(this.gridResult);
            this.split.Panel2.Controls.Add(this.grpResult);
            this.split.Size = new System.Drawing.Size(1264, 663);
            this.split.SplitterDistance = 840;
            this.split.TabIndex = 1;
            //
            // viewer
            //
            this.viewer.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(32)))), ((int)(((byte)(32)))), ((int)(((byte)(32)))));
            this.viewer.Dock = System.Windows.Forms.DockStyle.Fill;
            this.viewer.Font = new System.Drawing.Font("Consolas", 10F, System.Drawing.FontStyle.Bold);
            this.viewer.ForeColor = System.Drawing.Color.White;
            this.viewer.Location = new System.Drawing.Point(0, 0);
            this.viewer.Name = "viewer";
            this.viewer.Size = new System.Drawing.Size(840, 663);
            this.viewer.TabIndex = 0;
            this.viewer.RoiChanged += new System.EventHandler(this.viewer_RoiChanged);
            this.viewer.ImageMouseMove += new System.EventHandler<DLReadingTest.Controls.ImageMouseEventArgs>(this.viewer_ImageMouseMove);
            //
            // gridResult
            //
            this.gridResult.AllowUserToAddRows = false;
            this.gridResult.AllowUserToDeleteRows = false;
            this.gridResult.AllowUserToResizeRows = false;
            this.gridResult.AutoSizeColumnsMode = System.Windows.Forms.DataGridViewAutoSizeColumnsMode.Fill;
            this.gridResult.BackgroundColor = System.Drawing.SystemColors.Window;
            this.gridResult.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this.gridResult.Columns.AddRange(new System.Windows.Forms.DataGridViewColumn[] {
            this.colIndex,
            this.colLine,
            this.colChar,
            this.colConf,
            this.colX,
            this.colY,
            this.colW,
            this.colH,
            this.colJudge,
            this.colNote});
            this.gridResult.Dock = System.Windows.Forms.DockStyle.Fill;
            this.gridResult.Location = new System.Drawing.Point(0, 120);
            this.gridResult.MultiSelect = false;
            this.gridResult.Name = "gridResult";
            this.gridResult.ReadOnly = true;
            this.gridResult.RowHeadersVisible = false;
            this.gridResult.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.FullRowSelect;
            this.gridResult.Size = new System.Drawing.Size(420, 543);
            this.gridResult.TabIndex = 1;
            this.gridResult.SelectionChanged += new System.EventHandler(this.gridResult_SelectionChanged);
            //
            // colIndex
            //
            this.colIndex.FillWeight = 40F;
            this.colIndex.HeaderText = "#";
            this.colIndex.Name = "colIndex";
            this.colIndex.ReadOnly = true;
            //
            // colLine
            //
            this.colLine.FillWeight = 40F;
            this.colLine.HeaderText = "줄";
            this.colLine.Name = "colLine";
            this.colLine.ReadOnly = true;
            //
            // colChar
            //
            dataGridViewCellStyle1.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleCenter;
            dataGridViewCellStyle1.Font = new System.Drawing.Font("Consolas", 11F, System.Drawing.FontStyle.Bold);
            this.colChar.DefaultCellStyle = dataGridViewCellStyle1;
            this.colChar.FillWeight = 50F;
            this.colChar.HeaderText = "문자";
            this.colChar.Name = "colChar";
            this.colChar.ReadOnly = true;
            //
            // colConf
            //
            this.colConf.FillWeight = 60F;
            this.colConf.HeaderText = "신뢰도";
            this.colConf.Name = "colConf";
            this.colConf.ReadOnly = true;
            //
            // colX
            //
            this.colX.FillWeight = 55F;
            this.colX.HeaderText = "X";
            this.colX.Name = "colX";
            this.colX.ReadOnly = true;
            //
            // colY
            //
            this.colY.FillWeight = 55F;
            this.colY.HeaderText = "Y";
            this.colY.Name = "colY";
            this.colY.ReadOnly = true;
            //
            // colW
            //
            this.colW.FillWeight = 50F;
            this.colW.HeaderText = "W";
            this.colW.Name = "colW";
            this.colW.ReadOnly = true;
            //
            // colH
            //
            this.colH.FillWeight = 50F;
            this.colH.HeaderText = "H";
            this.colH.Name = "colH";
            this.colH.ReadOnly = true;
            //
            // colJudge
            //
            this.colJudge.FillWeight = 50F;
            this.colJudge.HeaderText = "판정";
            this.colJudge.Name = "colJudge";
            this.colJudge.ReadOnly = true;
            //
            // colNote
            //
            this.colNote.FillWeight = 140F;
            this.colNote.HeaderText = "비고";
            this.colNote.Name = "colNote";
            this.colNote.ReadOnly = true;
            //
            // grpResult
            //
            this.grpResult.Controls.Add(this.txtResult);
            this.grpResult.Dock = System.Windows.Forms.DockStyle.Top;
            this.grpResult.Location = new System.Drawing.Point(0, 0);
            this.grpResult.Name = "grpResult";
            this.grpResult.Padding = new System.Windows.Forms.Padding(6);
            this.grpResult.Size = new System.Drawing.Size(420, 120);
            this.grpResult.TabIndex = 0;
            this.grpResult.TabStop = false;
            this.grpResult.Text = "읽은 결과";
            //
            // txtResult
            //
            this.txtResult.BackColor = System.Drawing.Color.Black;
            this.txtResult.Dock = System.Windows.Forms.DockStyle.Fill;
            this.txtResult.Font = new System.Drawing.Font("Consolas", 18F, System.Drawing.FontStyle.Bold);
            this.txtResult.ForeColor = System.Drawing.Color.Lime;
            this.txtResult.Location = new System.Drawing.Point(6, 20);
            this.txtResult.Multiline = true;
            this.txtResult.Name = "txtResult";
            this.txtResult.ReadOnly = true;
            this.txtResult.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            this.txtResult.Size = new System.Drawing.Size(408, 94);
            this.txtResult.TabIndex = 0;
            //
            // statusStrip
            //
            this.statusStrip.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.lblStatusFile,
            this.lblStatusSize,
            this.lblStatusRoi,
            this.lblStatusTime,
            this.lblStatusMouse,
            this.lblStatusModel});
            this.statusStrip.Location = new System.Drawing.Point(0, 739);
            this.statusStrip.Name = "statusStrip";
            this.statusStrip.Size = new System.Drawing.Size(1264, 22);
            this.statusStrip.TabIndex = 2;
            //
            // lblStatusFile
            //
            this.lblStatusFile.Name = "lblStatusFile";
            this.lblStatusFile.Size = new System.Drawing.Size(55, 17);
            this.lblStatusFile.Text = "이미지 없음";
            //
            // lblStatusSize
            //
            this.lblStatusSize.BorderSides = System.Windows.Forms.ToolStripStatusLabelBorderSides.Left;
            this.lblStatusSize.Name = "lblStatusSize";
            this.lblStatusSize.Size = new System.Drawing.Size(4, 17);
            //
            // lblStatusRoi
            //
            this.lblStatusRoi.BorderSides = System.Windows.Forms.ToolStripStatusLabelBorderSides.Left;
            this.lblStatusRoi.Name = "lblStatusRoi";
            this.lblStatusRoi.Size = new System.Drawing.Size(70, 17);
            this.lblStatusRoi.Text = "ROI: 전체";
            //
            // lblStatusTime
            //
            this.lblStatusTime.BorderSides = System.Windows.Forms.ToolStripStatusLabelBorderSides.Left;
            this.lblStatusTime.Name = "lblStatusTime";
            this.lblStatusTime.Size = new System.Drawing.Size(4, 17);
            //
            // lblStatusMouse
            //
            this.lblStatusMouse.BorderSides = System.Windows.Forms.ToolStripStatusLabelBorderSides.Left;
            this.lblStatusMouse.Name = "lblStatusMouse";
            this.lblStatusMouse.Size = new System.Drawing.Size(4, 17);
            //
            // lblStatusModel
            //
            this.lblStatusModel.Name = "lblStatusModel";
            this.lblStatusModel.Size = new System.Drawing.Size(1000, 17);
            this.lblStatusModel.Spring = true;
            this.lblStatusModel.Text = "모델 없음";
            this.lblStatusModel.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            //
            // timerRotate
            //
            this.timerRotate.Interval = 1000;
            this.timerRotate.Tick += new System.EventHandler(this.timerRotate_Tick);
            //
            // MainForm
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 12F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(1264, 761);
            this.Controls.Add(this.split);
            this.Controls.Add(this.flowTop);
            this.Controls.Add(this.statusStrip);
            this.KeyPreview = true;
            this.MinimumSize = new System.Drawing.Size(900, 500);
            this.Name = "MainForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "DL Reading Test - YOLO 문자 인식";
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.MainForm_FormClosing);
            this.Load += new System.EventHandler(this.MainForm_Load);
            this.flowTop.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.numInterval)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numConf)).EndInit();
            this.split.Panel1.ResumeLayout(false);
            this.split.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.split)).EndInit();
            this.split.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.gridResult)).EndInit();
            this.grpResult.ResumeLayout(false);
            this.grpResult.PerformLayout();
            this.statusStrip.ResumeLayout(false);
            this.statusStrip.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.FlowLayoutPanel flowTop;
        private System.Windows.Forms.Button btnOpenFolder;
        private System.Windows.Forms.Button btnOpenModel;
        private System.Windows.Forms.Label sep1;
        private System.Windows.Forms.Button btnPrev;
        private System.Windows.Forms.Button btnPlay;
        private System.Windows.Forms.Button btnNext;
        private System.Windows.Forms.Label lblInterval;
        private System.Windows.Forms.NumericUpDown numInterval;
        private System.Windows.Forms.Label sep2;
        private System.Windows.Forms.CheckBox chkRoiEdit;
        private System.Windows.Forms.Button btnClearRoi;
        private System.Windows.Forms.Label sep3;
        private System.Windows.Forms.Button btnRead;
        private System.Windows.Forms.CheckBox chkAutoRead;
        private System.Windows.Forms.Label lblConf;
        private System.Windows.Forms.NumericUpDown numConf;
        private System.Windows.Forms.Label lblExpected;
        private System.Windows.Forms.TextBox txtExpected;
        private System.Windows.Forms.Button btnVerify;
        private System.Windows.Forms.SplitContainer split;
        private Controls.ImageViewer viewer;
        private System.Windows.Forms.DataGridView gridResult;
        private System.Windows.Forms.DataGridViewTextBoxColumn colIndex;
        private System.Windows.Forms.DataGridViewTextBoxColumn colLine;
        private System.Windows.Forms.DataGridViewTextBoxColumn colChar;
        private System.Windows.Forms.DataGridViewTextBoxColumn colConf;
        private System.Windows.Forms.DataGridViewTextBoxColumn colX;
        private System.Windows.Forms.DataGridViewTextBoxColumn colY;
        private System.Windows.Forms.DataGridViewTextBoxColumn colW;
        private System.Windows.Forms.DataGridViewTextBoxColumn colH;
        private System.Windows.Forms.DataGridViewTextBoxColumn colJudge;
        private System.Windows.Forms.DataGridViewTextBoxColumn colNote;
        private System.Windows.Forms.GroupBox grpResult;
        private System.Windows.Forms.TextBox txtResult;
        private System.Windows.Forms.StatusStrip statusStrip;
        private System.Windows.Forms.ToolStripStatusLabel lblStatusFile;
        private System.Windows.Forms.ToolStripStatusLabel lblStatusSize;
        private System.Windows.Forms.ToolStripStatusLabel lblStatusRoi;
        private System.Windows.Forms.ToolStripStatusLabel lblStatusTime;
        private System.Windows.Forms.ToolStripStatusLabel lblStatusMouse;
        private System.Windows.Forms.ToolStripStatusLabel lblStatusModel;
        private System.Windows.Forms.Timer timerRotate;
    }
}
