;;
;; Compiling (if required)
;;
Create a folder "HyperGrid" in your addon-modules folder in the source and drop all the files in this directory in it
Run prebuild.bat and compile.bat and it will build the source

;;
;; To install in standalone mode
;;
Drop StandaloneHypergrid.ini into Configuration/Modules/ and restart Aurora and it will function, no configuration required

;;
;; To install in grid mode (Aurora.Server)
;;
In AuroraServer.ini, Change Include-Main to

Include-Main = AuroraServerConfiguration/HGMain.ini

and copy the file HGMain into the folder AuroraServerConfiguration/

;;
;; To install in grid mode (region)
;;

Drop GridModeRegion.ini into Configuration/Modules/ and restart Aurora