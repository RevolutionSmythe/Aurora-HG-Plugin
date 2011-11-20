;;
;; Compiling (only required if you did not use the build.am file)
;;
Create a folder "HyperGrid" in your addon-modules folder in the source and drop all the files in this directory in it
Run prebuild.bat and compile.bat and it will build the source

;;
;; To install in standalone mode
;;
Drop StandaloneHypergrid.ini 
or
StandaloneIWCHypergrid.ini (and enable IWC in StandaloneCommon.ini.example)
into Configuration/Modules/ and restart Aurora and it will function, no configuration required

;;
;; To install in grid mode (Aurora.Server)
;;
In AuroraServer.ini, if you just want to enable HG only, change Include-Main to

Include-Main = AuroraServerConfiguration/HGMain.ini
and copy the file HGMain into the folder AuroraServerConfiguration/

otherwise, uncomment Include-IWCHGMain and comment Include-Main to enable both HG and IWC

;;
;; To install in grid mode (region)
;;

Drop GridModeRegion.ini into Configuration/Modules/ and restart Aurora