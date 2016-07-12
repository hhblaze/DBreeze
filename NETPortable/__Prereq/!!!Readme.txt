Here is completely portable DBreeze v 1.75 and higher.
DBreeze\NETPortable\__Prereq\DBreeze.dll

Can be referenced from .NET Portable library.

To instantiate DBreeze's file system use
DBreeze\NETPortable\__Prereq\FSFactory.cs  
class.

Example1 in DBreeze\NETPortable\__Prereq\App1.zip

Example2 UWP:

1. Non portable project (iOS, Android, UWP)
instantiates new DBreeze instance:

 sealed partial class App : Application
    {
        public static App MAIN = null;

        public DBreezeEngine engine = null;
     
        public App()
        {
            this.InitializeComponent();
            this.Suspending += OnSuspending;
            
            if (engine == null)
            {
                FSFactory fsf = new FSFactory();

                Windows.Storage.StorageFolder localFolder = Windows.Storage.ApplicationData.Current.LocalFolder;

                engine = new DBreezeEngine(new DBreezeConfiguration()
                {
                    DBreezeDataFolderName = localFolder.Path,
                    FSFactory = fsf
                });
            }


            App.MAIN = this;
        }

        
        protected override void OnLaunched(LaunchActivatedEventArgs e)
        {
		
		.....

2. Referencing instantiated DBreeze instance to portable class:


namespace App1  (Portable class)
{
    public partial class App : Application
    {
        public static App MAIN = null;
        public DBreeze.DBreezeEngine engine = null; (non-instantiated DBreeze)
		
		...
		
From UWP giving ref of DBreez to portable class:
		instance of App1.App.engine = App.MAIN.engine (UWP engine);
		
		

