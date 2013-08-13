using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

using DBreeze.Storage;

namespace VisualTester
{
    /// <summary>
    /// Interaction logic for DBreezeBackupRestorer.xaml
    /// </summary>
    public partial class DBreezeBackupRestorer : UserControl
    {
        //DBreezeBackupRestorer
        BackupRestorer restorer = new BackupRestorer();

        public DBreezeBackupRestorer()
        {
            InitializeComponent();

            restorer.OnRestore += new Action<BackupRestorer.BackupRestorationProcess>(restorer_OnRestore);
        }
        

        private void btRestoreStart_Click(object sender, RoutedEventArgs e)
        {
            restorer.BackupFolder = this.tbBackupFolder.Text;
            restorer.DataBaseFolder = this.tbDbFolder.Text;

            restorer.StartRestoration();
        }

        void restorer_OnRestore(BackupRestorer.BackupRestorationProcess obj)
        {
            //Console.WriteLine(obj.ReadinessInProcent.ToString() + "%");

            if (obj.Finished)
                MessageBox.Show("restoration completed");
        }
        
    }
}
